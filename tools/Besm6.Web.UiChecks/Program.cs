using Microsoft.Playwright;
using System.Text;
using System.Text.Json;

var output = Path.GetFullPath("tests-run/web-ui-checks");
Directory.CreateDirectory(output);
using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var results = new List<object>();
var errors = new List<string>();
var layouts = new List<JsonElement>();
int failures = 0;
string root = (Environment.GetEnvironmentVariable("BESM6_WEB_URL") ?? "http://127.0.0.1:8080").TrimEnd('/');
// An override preserves direct ASP.NET Core development without nginx.
string? directSimulator = Environment.GetEnvironmentVariable("BESM6_WEB_SIMULATOR_URL");
string simulatorUrl = directSimulator ?? root + "/simulator/";
const string tiny = "*assem\natx 1234\nstop\n*end file\n";
const string loop = "*assem\nuj 1000\n*end file\n";
static ulong Asm(string text) => new Besm6.Assembler.Assembly.MadlenAssembler()
    .AssembleWord(text.Contains(',') ? text : text + ", atx");
static string Raw(IEnumerable<ulong> words, int address = 512) => "*trans-main:" + Convert.ToString(address, 8) + "\n"
    + string.Join('\n', words.Select(w => "`" + Convert.ToString((long)w, 8).PadLeft(16, '0'))) + "\n*end file\n";
static ulong[] Bytes(string text)
{
    var bytes = Encoding.Latin1.GetBytes(text + '\0');
    return Enumerable.Range(0, (bytes.Length + 5) / 6).Select(i =>
    {
        ulong word = 0;
        for (int j = 0; j < 6; j++) word = (word << 8) | (i * 6 + j < bytes.Length ? bytes[i * 6 + j] : 0UL);
        return word;
    }).ToArray();
}

async Task<IPage> Open(IBrowserContext context)
{
    var page = await context.NewPageAsync();
    page.SetDefaultTimeout(20000);
    page.PageError += (_, message) => errors.Add(message);
    await page.AddInitScriptAsync("""
    window.__sockets=[]; window.__events=[]; const NativeWebSocket=window.WebSocket;
    window.WebSocket=class extends NativeWebSocket {
      constructor(...args) {
        super(...args); window.__sockets.push(this);
        this.addEventListener('message', e => {
          if (typeof e.data !== 'string') return;
          for (const part of e.data.split(String.fromCharCode(30)).filter(Boolean)) {
            const frame=JSON.parse(part);
            if(frame.target === 'sessionReady') window.__sessionId = frame.arguments[0];
            if(frame.target === 'workerEvent') {window.__events.push({...frame.arguments[0],at:performance.now()}); if(window.__events.length>1000)window.__events.shift();}
          }
        });
      }
    };
    """);
    await page.GotoAsync(simulatorUrl);
    try { await page.WaitForFunctionAsync("document.getElementById('connection').dataset.state === 'Connected'"); }
    catch
    {
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "connection-failure.png"), FullPage = true });
        Console.WriteLine("CONNECTION " + await page.EvaluateAsync<string>("JSON.stringify({text:document.getElementById('connection').textContent,language:document.documentElement.lang,error:document.getElementById('error').textContent})"));
        Console.WriteLine("ERRORS " + JsonSerializer.Serialize(errors));
        throw;
    }
    return page;
}
async Task Upload(IPage page, string text = tiny, string name = "smoke.dub")
{
    await page.WaitForFunctionAsync("!document.getElementById('file').disabled");
    await page.Locator("#file").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "text/plain", Buffer = Encoding.UTF8.GetBytes(text) });
    await page.WaitForFunctionAsync("name => document.getElementById('fileName').textContent === name", name);
}
Task Status(IPage page, string state) => page.WaitForFunctionAsync("state => document.getElementById('status').textContent === state", state);
async Task<string> GuestOutput(IPage page) => await page.Locator("#printOutput").TextContentAsync()
    + "\n" + await page.Locator("#terminalOutput").TextContentAsync();
async Task Check(string name, Func<IPage, IBrowserContext, Task> action)
{
    if (args.Length > 0 && !args.Contains(name)) return;
    await using var context = await browser.NewContextAsync(new() { Locale = "en-US", ViewportSize = new() { Width = 1440, Height = 900 } });
    var page = await Open(context);
    try { await action(page, context); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex)
    {
        failures++;
        var state = page.IsClosed ? "Page closed" : await page.EvaluateAsync<string>("JSON.stringify({status:document.getElementById('status').textContent,error:document.getElementById('error').textContent,connection:document.getElementById('connection').textContent})");
        results.Add(new { name, passed = false, error = ex.ToString(), state });
        if (!page.IsClosed) Console.WriteLine("DIAGNOSTICS " + await page.EvaluateAsync<string>("JSON.stringify({count:document.getElementById('instructionCount').textContent,events:window.__events.filter(e=>e.type!=='memory').slice(-10)})"));
        Console.WriteLine("FAIL " + name + " " + state + " " + ex.Message.Split('\n')[0]);
        if (!page.IsClosed) await page.ScreenshotAsync(new() { Path = Path.Combine(output, name + "-failure.png"), FullPage = true });
    }
}

if ((args.Length == 0 && directSimulator is null) || args.Contains("landing"))
{
    await using var context = await browser.NewContextAsync(new() { Locale = "en-US" });
    var page = await context.NewPageAsync();
    page.PageError += (_, message) => errors.Add(message);
    try
    {
        var response = await page.GotoAsync(root + "/");
        if (response?.Status != 200) throw new Exception("Landing did not return HTTP 200");
        if (await page.Locator("html").GetAttributeAsync("lang") != "ru") throw new Exception("Landing language is not Russian");
        if (await page.Locator("h1").CountAsync() != 1) throw new Exception("Expected one page heading");
        foreach (int width in new[] { 1440, 1024, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 900);
            // Scroll lazy images into view before checking their decoded dimensions.
            foreach (var picture in await page.Locator("img").AllAsync())
            {
                await picture.ScrollIntoViewIfNeededAsync();
                await picture.EvaluateAsync("e => e.decode()");
            }
            if (!await page.EvaluateAsync<bool>("[...document.images].every(i => i.complete && i.naturalWidth > 0 && i.alt.length > 0)"))
                throw new Exception("Landing has an unloaded or unlabeled image");
            if (await page.EvaluateAsync<int>("document.documentElement.scrollWidth") > width)
                throw new Exception("Landing overflows at " + width);
            await page.EvaluateAsync("scrollTo({top:0,behavior:'instant'})");
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"landing-{width}.png"), FullPage = true });
            if (width == 1440)
            {
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, "landing-preview.png") });
                await page.Locator(".architecture-grid > article:last-child").ScreenshotAsync(new() { Path = Path.Combine(output, "landing-device.png") });
            }
        }
        foreach (var link in await page.Locator("a[href^='#']").AllAsync())
        {
            var target = await link.GetAttributeAsync("href");
            if (await page.Locator(target!).CountAsync() != 1) throw new Exception("Broken section link: " + target);
        }
        await page.Keyboard.PressAsync("Tab");
        if (!await page.EvaluateAsync<bool>("document.activeElement.classList.contains('skip-link')"))
            throw new Exception("Keyboard navigation does not start with the skip link");
        string axePath = Path.Combine(output, "axe.min.js");
        if (!File.Exists(axePath))
        {
            using var http = new HttpClient();
            await File.WriteAllTextAsync(axePath, await http.GetStringAsync("https://cdn.jsdelivr.net/npm/axe-core@4.10.3/axe.min.js"));
        }
        await page.AddScriptTagAsync(new() { Path = axePath });
        var violations = await page.EvaluateAsync<JsonElement>("async()=>{const result=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}});return result.violations;}");
        await File.WriteAllTextAsync(Path.Combine(output, "landing-accessibility.json"), violations.ToString());
        if (violations.GetArrayLength() > 0) throw new Exception("Landing accessibility violations: " + violations);
        // The canonical redirect must retain the public port and query string.
        var redirect = await page.APIRequest.GetAsync(root + "/simulator?probe=1", new() { MaxRedirects = 0 });
        if (redirect.Status != 308 || redirect.Headers["location"] != "/simulator/?probe=1")
            throw new Exception("Simulator canonical redirect is incorrect");
        await page.Locator(".hero .button").ClickAsync();
        await page.WaitForURLAsync(root + "/simulator/");
        await page.WaitForFunctionAsync("document.getElementById('connection')?.textContent === 'Connected'");
        await page.Locator("nav a[href='/']").ClickAsync();
        await page.WaitForURLAsync(root + "/");
        results.Add(new { name = "landing", passed = true });
        Console.WriteLine("PASS landing");
    }
    catch (Exception ex)
    {
        failures++;
        results.Add(new { name = "landing", passed = false, error = ex.ToString() });
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "landing-failure.png"), FullPage = true });
        Console.WriteLine("FAIL landing " + ex.Message);
    }
}

// This scenario deliberately uses only browser-visible state, so it also works
// against Linux containers or a remote host without access to session files.
await Check("deployment-smoke", async (first, _) =>
{
    if (!await first.EvaluateAsync<bool>("window.__sockets.some(s => s.readyState === WebSocket.OPEN && new URL(s.url).pathname === '/emulator')"))
        throw new Exception("SignalR is not using an open WebSocket");
    var media = await first.APIRequest.GetAsync(root + "/api/media");
    if (!media.Ok || (await media.JsonAsync())?.GetArrayLength() != 5) throw new Exception("Published tape images are missing");
    var template = await first.APIRequest.GetAsync(root + "/api/machine-template");
    if (!template.Ok) throw new Exception("Published machine template is missing");

    await using var otherContext = await browser.NewContextAsync(new() { Locale = "en-US" });
    var second = await Open(otherContext);
    if (await first.EvaluateAsync<string>("window.__sessionId") == await second.EvaluateAsync<string>("window.__sessionId"))
        throw new Exception("Two browsers share a session");
    await Upload(second);
    await second.ClickAsync("#run"); await Status(second, "Paused");
    await first.ClickAsync("#examplesTab");
    await first.WaitForFunctionAsync("[...document.getElementById('exampleSelect').options].some(o => o.value === 'fortran.dub')");
    await first.SelectOptionAsync("#exampleSelect", "fortran.dub");
    await first.WaitForFunctionAsync("document.getElementById('exampleSource').dataset.loaded === 'fortran.dub'");
    await first.ClickAsync("#exampleLoad");
    await first.CheckAsync("input[value=full]");
    await first.ClickAsync("#run"); await Status(first, "Completed");
    if (!(await GuestOutput(first)).Contains("HELLO, WORLD!", StringComparison.OrdinalIgnoreCase)) throw new Exception("Built-in Fortran example failed");
    if (await second.Locator("#status").InnerTextAsync() != "Paused") throw new Exception("First run changed the other session");
    await second.ClickAsync("#resume"); await Status(second, "Completed");
    if (!(await GuestOutput(first)).Contains("HELLO, WORLD!", StringComparison.OrdinalIgnoreCase)) throw new Exception("Second run changed the first output");
});

await Check("languages-and-guide", async (page, _) =>
{
    string[] order = await page.Locator("[role=tablist] [role=tab]").EvaluateAllAsync<string[]>("tabs => tabs.map(t => t.id)");
    if (!order.Take(3).SequenceEqual(new[] { "manualTab", "examplesTab", "printTab" })) throw new Exception("User guide and Examples must be the first tabs");
    if (!await page.Locator("#manualPane").IsVisibleAsync()) throw new Exception("The user guide is not the initial view");
    await page.Locator("#manualPane [data-language=en] [data-show-tab=examplesTab]").ClickAsync();
    await page.Locator("#examplesPane.show").WaitForAsync();
    await page.WaitForFunctionAsync("[...document.getElementById('exampleSelect').options].some(o => o.value === 'fortran.dub')");
    await page.SelectOptionAsync("#exampleSelect", "fortran.dub");
    await page.WaitForFunctionAsync("document.getElementById('exampleSource').dataset.loaded === 'fortran.dub'");
    await page.ClickAsync("#exampleLoad");
    await page.CheckAsync("input[value=full]"); await page.ClickAsync("#run"); await Status(page, "Completed");
    await page.ClickAsync("#printTab");
    if (!await page.Locator("#printPane").IsVisibleAsync()) throw new Exception("Printer did not open");
    string guest = await GuestOutput(page), source = await page.Locator("#exampleSource").TextContentAsync() ?? "";
    string session = await page.EvaluateAsync<string>("window.__sessionId");
    int sockets = await page.EvaluateAsync<int>("window.__sockets.length");
    await page.ClickAsync("#editorTab");
    await page.FillAsync("#sourceText", "Run\nConnected\nПример\n<not-markup>");
    await page.SelectOptionAsync("#languageSelect", "ru");
    if (await page.Locator("html").GetAttributeAsync("lang") != "ru" || await page.Locator("#run").InnerTextAsync() != "Пуск"
        || await page.Locator("#connection").InnerTextAsync() != "Подключено" || await page.Locator("#status").InnerTextAsync() != "Завершено")
        throw new Exception("Russian controls or dynamic status were not translated");
    if (!(await page.Locator("#instructionCount").InnerTextAsync()).Contains("команд")) throw new Exception("Instruction counter was not translated");
    if (await GuestOutput(page) != guest || await page.Locator("#exampleSource").TextContentAsync() != source
        || await page.Locator("#sourceText").InputValueAsync() != "Run\nConnected\nПример\n<not-markup>")
        throw new Exception("Changing language modified guest output or source");
    if (await page.EvaluateAsync<string>("window.__sessionId") != session || await page.EvaluateAsync<int>("window.__sockets.length") != sockets)
        throw new Exception("Changing language restarted the session");
    string guideAxePath = Path.Combine(output, "axe.min.js");
    if (!File.Exists(guideAxePath))
    {
        using var http = new HttpClient();
        await File.WriteAllTextAsync(guideAxePath, await http.GetStringAsync("https://cdn.jsdelivr.net/npm/axe-core@4.10.3/axe.min.js"));
    }
    foreach (string language in new[] { "ru", "en" })
    {
        await page.SelectOptionAsync("#languageSelect", language);
        await page.ClickAsync("#manualTab");
        await page.Locator("#manualPane.show.active").WaitForAsync();
        if (!await page.Locator($"#manualPane [data-language={language}]").IsVisibleAsync()) throw new Exception("Wrong guide language");
        foreach (int width in new[] { 1440, 768, 320 })
        {
            await page.SetViewportSizeAsync(width, 900);
            if (await page.EvaluateAsync<int>("document.documentElement.scrollWidth") > width) throw new Exception("Translated layout overflows: " + language + " " + width);
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"guide-{language}-{width}.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
        }
        await page.AddScriptTagAsync(new() { Path = guideAxePath });
        var violations = await page.EvaluateAsync<JsonElement>("async()=>{const r=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}});return r.violations;}");
        if (violations.GetArrayLength() > 0) throw new Exception("Guide accessibility: " + language + " " + violations);
    }
    await page.SelectOptionAsync("#languageSelect", "ru");
    await page.ReloadAsync();
    await page.WaitForFunctionAsync("document.getElementById('connection').dataset.state === 'Connected'");
    if (await page.Locator("#languageSelect").InputValueAsync() != "ru" || await page.Locator("html").GetAttributeAsync("lang") != "ru")
        throw new Exception("The language preference did not survive reload");
    await page.SelectOptionAsync("#languageSelect", "en");
    if (await page.Locator("#run").InnerTextAsync() != "Run") throw new Exception("English was not restored");
    await using var russianContext = await browser.NewContextAsync(new() { Locale = "ru-RU" });
    var russian = await Open(russianContext);
    if (await russian.Locator("html").GetAttributeAsync("lang") != "ru") throw new Exception("Browser language was not detected");
});

await Check("debug", async (page, _) =>
{
    await Upload(page);
    await page.ClickAsync("#run"); await Status(page, "Paused");
    await page.ClickAsync("#step");
    await page.WaitForFunctionAsync("/^1 instructions?$/.test(document.getElementById('instructionCount').textContent)");
    await page.ClickAsync("#resume"); await Status(page, "Completed");
    if (!string.IsNullOrEmpty(await page.Locator("#error").InnerTextAsync())) throw new Exception("Unexpected error");
});
await Check("full", async (page, _) =>
{
    await Upload(page, File.ReadAllText("tests/Besm6.Integration.Tests/raw/hello.dub"));
    await page.CheckAsync("input[value=full]");
    await page.ClickAsync("#run"); await Status(page, "Completed");
    if (!(await page.Locator("#printOutput").InnerTextAsync()).Contains("HI")) throw new Exception("Expected guest output HI");
});
foreach (var (path, expected) in new[] { ("examples/fortran.dub", "HELLO, WORLD!"), ("examples/forex.dub", "HELLO, WORLD!"), ("examples/b/hello.dub", "HELLO, B!") })
    await Check("source-" + Path.GetFileNameWithoutExtension(path), async (page, _) =>
    {
        await Upload(page, File.ReadAllText(path), Path.GetFileName(path));
        await page.CheckAsync("input[value=full]");
        await page.ClickAsync("#run"); await Status(page, "Completed");
        string text = await GuestOutput(page);
        await File.WriteAllTextAsync(Path.Combine(output, Path.GetFileName(path) + ".out.txt"), text);
        if (!text.Split('\n').Any(line => line.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Expected printed line: " + expected);
    });
await Check("pause-step-stop", async (page, _) =>
{
    await Upload(page, loop); await page.ClickAsync("#run"); await Status(page, "Paused");
    await page.ClickAsync("#resume"); await Status(page, "Running");
    await page.ClickAsync("#pause"); await Status(page, "Paused");
    await page.ClickAsync("#step"); await page.ClickAsync("#stop"); await Status(page, "Halted");
    await page.ClickAsync("#run"); await Status(page, "Paused");
});
await Check("invalid-job", async (page, _) =>
{
    await Upload(page, "`9999\n"); await page.ClickAsync("#run"); await Status(page, "Failed");
    if (string.IsNullOrWhiteSpace(await page.Locator("#error").InnerTextAsync())) throw new Exception("Missing error");
    await Upload(page); await page.ClickAsync("#run"); await Status(page, "Paused");
});
await Check("instruction-limit", async (page, _) =>
{
    await Upload(page, loop); await page.CheckAsync("input[value=full]");
    await page.FillAsync("#instructionLimit", "10"); await page.ClickAsync("#run"); await Status(page, "Failed");
    if (string.IsNullOrWhiteSpace(await page.Locator("#error").InnerTextAsync())) throw new Exception("Limit reached without explanation");
});
await Check("invalid-options", async (page, _) =>
{
    await Upload(page); await page.FillAsync("#instructionLimit", "1000000001"); await page.ClickAsync("#run");
    await page.WaitForFunctionAsync("document.getElementById('error').textContent.length > 0");
    await page.FillAsync("#instructionLimit", "20"); await page.ClickAsync("#run"); await Status(page, "Paused");
});
await Check("reconnect", async (page, context) =>
{
    await Upload(page);
    await page.EvaluateAsync("window.__sockets[0].close(1000, 'Browser reconnect check')");
    await page.WaitForFunctionAsync("window.__sockets.length > 1 && document.getElementById('connection').textContent === 'Connected' && !document.getElementById('file').disabled", null, new() { Timeout = 15000 });
    await Upload(page); await page.ClickAsync("#run"); await Status(page, "Paused");
});
await Check("multiple-sessions", async (first, _) =>
{
    await using var secondContext = await browser.NewContextAsync(new() { Locale = "en-US" });
    await using var thirdContext = await browser.NewContextAsync(new() { Locale = "en-US" });
    var others = await Task.WhenAll(Open(secondContext), Open(thirdContext));
    var second = others[0]; var third = others[1];
    string Marker(int value) => "*trans-main:1000\n`0010100200000000\n`0330000000000000\n`000000000000000" + value + "\n*end file\n";
    await Task.WhenAll(Upload(first, Marker(1), "first.dub"), Upload(second, Marker(2), "second.dub"),
        Upload(third, File.ReadAllText("tests/Besm6.Integration.Tests/raw/hello.dub"), "third.dub"));
    var ids = await Task.WhenAll(new[] { first, second, third }.Select(p => p.EvaluateAsync<string>("window.__sessionId")));
    if (ids.Distinct().Count() != 3) throw new Exception("Sessions share an ID");
    foreach (string id in ids) if (!Directory.Exists(Path.Combine(Path.GetTempPath(), "besm6-web", id))) throw new Exception("Session directory missing");
    await third.CheckAsync("input[value=full]");
    await Task.WhenAll(first.ClickAsync("#run"), second.ClickAsync("#run"), third.ClickAsync("#run"));
    await Task.WhenAll(Status(first, "Paused"), Status(second, "Paused"), Status(third, "Completed"));
    await Task.WhenAll(first.ClickAsync("#step"), second.ClickAsync("#step"));
    string valueScript = "value => [...document.querySelectorAll('#registers .register')].find(e=>e.querySelector('dt').textContent==='A').querySelector('dd').textContent === value";
    await Task.WhenAll(first.WaitForFunctionAsync(valueScript, "0000000000000001"), second.WaitForFunctionAsync(valueScript, "0000000000000002"));
    await second.FillAsync("#address", "1002"); await second.PressAsync("#address", "Enter");
    await second.WaitForFunctionAsync("document.getElementById('memory').textContent.startsWith('01002  0000000000000002')");
    if ((await third.Locator("#printOutput").InnerTextAsync()).Trim() != "HI") throw new Exception("Output mixed between sessions");
    await first.CloseAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (Directory.Exists(Path.Combine(Path.GetTempPath(), "besm6-web", ids[0]))) await Task.Delay(100, timeout.Token);
    await second.ClickAsync("#resume"); await Status(second, "Completed");
    if (!(await third.Locator("#printOutput").InnerTextAsync()).Contains("HI")) throw new Exception("Other session lost output");
    Console.WriteLine("SESSION IDS " + string.Join(", ", ids));
});
await Check("drop-upload-and-tabs", async (page, _) =>
{
    await page.EvaluateAsync("""
    text => {const data=new DataTransfer(); data.items.add(new File([text], 'dropped.dub', {type:'text/plain'})); document.getElementById('dropzone').dispatchEvent(new DragEvent('drop',{bubbles:true,dataTransfer:data}));}
    """, tiny);
    await page.WaitForFunctionAsync("document.getElementById('fileName').textContent === 'dropped.dub'");
    await page.ClickAsync("#run"); await Status(page, "Paused");
    if (await page.Locator("input[value=full]").IsEnabledAsync() || await page.Locator("#file").IsEnabledAsync()) throw new Exception("Active job settings remain editable");
    await page.ClickAsync("#punchTab");
    if (!await page.Locator("#punchPane").IsVisibleAsync()) throw new Exception("Punch tab not visible");
    await page.PressAsync("#punchTab", "ArrowLeft");
    await page.WaitForFunctionAsync("document.getElementById('printTab').getAttribute('aria-selected') === 'true'");
});
await Check("parallel-source-jobs", async (first, _) =>
{
    await using var secondContext = await browser.NewContextAsync(new() { Locale = "en-US" });
    await using var thirdContext = await browser.NewContextAsync(new() { Locale = "en-US" });
    var others = await Task.WhenAll(Open(secondContext), Open(thirdContext));
    var pages = new[] { first, others[0], others[1] };
    var programs = new[] { ("examples/fortran.dub", "Hello, World!", "HELLO, WORLD1!"),
        ("examples/forex.dub", "Hello, World!", "HELLO, WORLD2!"), ("examples/b/hello.dub", "Hello, B!", "HELLO, WORLD3!") };
    await Task.WhenAll(pages.Select(async (page, i) =>
    {
        var (path, original, marker) = programs[i];
        await Upload(page, File.ReadAllText(path).Replace(original, marker), Path.GetFileName(path));
        await page.CheckAsync("input[value=full]");
    }));
    await Task.WhenAll(pages.Select(p => p.ClickAsync("#run")));
    await Task.WhenAll(pages.Select(async (p, i) =>
    {
        try { await Status(p, "Completed"); }
        catch
        {
            string details = await p.EvaluateAsync<string>("JSON.stringify({status:document.getElementById('status').textContent,error:document.getElementById('error').textContent,output:document.getElementById('printOutput').textContent})");
            Console.WriteLine("PARALLEL FAILURE " + i + " " + details);
            throw;
        }
    }));
    for (int i = 0; i < pages.Length; i++)
    {
        string text = await GuestOutput(pages[i]);
        await File.WriteAllTextAsync(Path.Combine(output, $"parallel-session-{i + 1}.txt"), text);
        if (!text.Split('\n').Any(line => line.Trim().Equals(programs[i].Item3, StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Missing output from session " + i + ": " + text[^Math.Min(text.Length, 400)..]);
        for (int j = 0; j < pages.Length; j++)
            if (i != j && text.Contains(programs[j].Item3, StringComparison.OrdinalIgnoreCase)) throw new Exception("Sessions share output");
    }
});
await Check("running-session-isolation", async (first, _) =>
{
    await using var secondContext = await browser.NewContextAsync(new() { Locale = "en-US" });
    var second = await Open(secondContext);
    await Task.WhenAll(new[] { first, second }.Select(async page =>
    {
        await Upload(page, loop);
        await page.CheckAsync("input[value=full]");
        await page.CheckAsync("#autoRefresh");
        await page.FillAsync("#instructionLimit", "1000000000");
        await page.ClickAsync("#run"); await Status(page, "Running");
    }));
    await Task.WhenAll(new[] { first, second }.Select(p => p.WaitForFunctionAsync("parseInt(document.getElementById('instructionCount').textContent)>10")));
    await first.ClickAsync("#pause"); await Status(first, "Paused");
    await Task.Delay(100);
    await first.ClickAsync("#inspect");
    await first.WaitForFunctionAsync("parseInt(document.getElementById('instructionCount').textContent)>0");
    string pausedCount = await first.Locator("#instructionCount").InnerTextAsync();
    await second.ClickAsync("#inspect");
    await second.WaitForFunctionAsync("parseInt(document.getElementById('instructionCount').textContent)>0");
    long runningCount = long.Parse((await second.Locator("#instructionCount").InnerTextAsync()).Split(' ')[0]);
    await Task.Delay(100);
    await second.ClickAsync("#inspect");
    await second.WaitForFunctionAsync("count=>parseInt(document.getElementById('instructionCount').textContent)>count", runningCount);
    await first.ClickAsync("#inspect");
    if (await first.Locator("#instructionCount").InnerTextAsync() != pausedCount) throw new Exception("Paused machine kept executing");
    await first.ClickAsync("#stop"); await Status(first, "Halted");
    if (await second.Locator("#status").InnerTextAsync() != "Running") throw new Exception("Stopping one session stopped another");
    await second.ClickAsync("#stop"); await Status(second, "Halted");
});
await Check("large-upload", async (page, _) =>
{
    string text = new string(' ', 10_000_000 - tiny.Length - 1) + "\n" + tiny;
    await Upload(page, text, "large.dub");
    string id = await page.EvaluateAsync<string>("window.__sessionId");
    var saved = await File.ReadAllBytesAsync(Path.Combine(Path.GetTempPath(), "besm6-web", id, "job.dub"));
    if (!saved.SequenceEqual(Encoding.UTF8.GetBytes(text))) throw new Exception("Uploaded contents differ");
    await page.ClickAsync("#run"); await Status(page, "Paused");
});
foreach (string mode in new[] { "full", "debug" })
    await Check("live-updates-" + mode, async (page, _) =>
    {
        string job = Raw([Asm("utm 1(1), ita 1"), Asm("atx 1004, uj 1000"), 0UL, 0UL, 0UL]);
        await Upload(page, job);
        await page.CheckAsync("input[value=" + mode + "]");
        await page.CheckAsync("#autoRefresh");
        await page.FillAsync("#instructionLimit", "1000000000");
        await page.ClickAsync("#run");
        if (mode == "debug") { await Status(page, "Paused"); await page.ClickAsync("#resume"); }
        await Status(page, "Running");
        await page.FillAsync("#address", "1004"); await page.ClickAsync("#inspect");
        await page.WaitForFunctionAsync("window.__events.filter(e=>e.type==='memory' && e.data.words[0].address === '1004').length>=5");
        var measurements = await page.EvaluateAsync<JsonElement>("""
        (()=>{const e=window.__events.filter(e=>e.type==='memory'&&e.data.words[0].address==='1004').slice(-4);return {intervals:e.slice(1).map((v,i)=>v.at-e[i].at),values:e.map(v=>v.data.words[0].hex)}})()
        """);
        var intervals = measurements.GetProperty("intervals").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        if (intervals.Average() is < 150 or > 700) throw new Exception("Unexpected refresh interval: " + measurements);
        if (measurements.GetProperty("values").EnumerateArray().Select(x => x.GetString()).Distinct().Count() < 2)
            throw new Exception("Memory is not changing during execution");
        await page.ClickAsync("#pause"); await Status(page, "Paused");
        int count = await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='memory').length");
        await Task.Delay(700);
        if (await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='memory').length") > count + 1)
            throw new Exception("Polling continues while paused");
        await page.ClickAsync("#stop"); await Status(page, "Halted");
        Console.WriteLine("REFRESH " + mode + " " + measurements);
    });
await Check("auto-refresh-toggle", async (page, _) =>
{
    if (await page.IsCheckedAsync("#autoRefresh")) throw new Exception("Auto-refresh should default to off");
    string job = Raw(new[] { Asm("xta 1002, *61 77777"), Asm("uj 1001"), 515UL }.Concat(Bytes("M0,0\nD100,100\n")));
    await Upload(page, job);
    await page.CheckAsync("input[value=full]"); await page.FillAsync("#instructionLimit", "1000000000");
    await page.ClickAsync("#plotterTab"); await page.ClickAsync("#run"); await Status(page, "Running");
    await page.WaitForFunctionAsync("Number(document.getElementById('plotterCanvas').dataset.segments)>0");
    await Task.Delay(650);
    if (await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='memory'||e.type==='registers').length") != 0)
        throw new Exception("Machine snapshots continue with auto-refresh off");
    await page.CheckAsync("#autoRefresh");
    await page.WaitForFunctionAsync("window.__events.filter(e=>e.type==='memory').length>=2");
    await page.UncheckAsync("#autoRefresh"); await Task.Delay(150);
    int count = await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='memory').length");
    await Task.Delay(700);
    if (await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='memory').length") != count)
        throw new Exception("Auto-refresh did not stop");
    await page.ClickAsync("#inspect");
    await page.WaitForFunctionAsync("count=>window.__events.filter(e=>e.type==='memory').length>count", count);
    await page.ClickAsync("#stop"); await Status(page, "Halted");
});
await Check("execution-throughput", async (page, _) =>
{
    await Upload(page, loop);
    var timings = new List<object>();
    foreach (string mode in new[] { "full", "debug" })
        foreach (bool refresh in new[] { false, true })
        {
            await page.CheckAsync("input[value=" + mode + "]");
            await page.Locator("#autoRefresh").SetCheckedAsync(refresh);
            await page.FillAsync("#instructionLimit", "10000000");
            await page.ClickAsync("#run");
            if (mode == "debug") { await Status(page, "Paused"); await page.ClickAsync("#resume"); }
            await Status(page, "Failed");
            if (!(await page.Locator("#instructionCount").TextContentAsync())!.StartsWith("10000000 "))
                throw new Exception("Instruction count changed on the fast execution path");
            var elapsed = await page.EvaluateAsync<double>("(()=>{const end=window.__events.findLast(e=>e.type==='completed');const start=window.__events.findLast(e=>e.type==='state'&&e.data.status==='Running');return (end.at-start.at)/1000})()");
            var sample = new { mode, refresh, seconds = elapsed, millionInstructionsPerSecond = 10 / elapsed };
            timings.Add(sample);
            Console.WriteLine("THROUGHPUT " + JsonSerializer.Serialize(sample));
        }
    await File.WriteAllTextAsync(Path.Combine(output, "throughput.json"), JsonSerializer.Serialize(timings, new JsonSerializerOptions { WriteIndented = true }));
});
await Check("guest-clock-checkbox", async (page, _) =>
{
    string[] program = ["*50 70210, atx 102", "vtm 77760(2), ntr 6", "vtm 60000(1), utc",
        "ntr 6, vlm 4(1)", "vlm 3(2), *50 70077", "atx 100, *50 70210", "atx 101, stop"];
    await Upload(page, Raw(program.Select(Asm), 1));
    await page.CheckAsync("#guestClock"); await page.CheckAsync("input[value=full]");
    await page.ClickAsync("#run"); await Status(page, "Completed");
    await page.FillAsync("#address", "100"); await page.ClickAsync("#inspect");
    string expected = Convert.ToString((long)Besm6.Architecture.Numerics.Word48.FromDouble(.04).Value, 8).PadLeft(16, '0');
    await page.WaitForFunctionAsync("expected=>document.getElementById('memory').textContent.startsWith('00100  '+expected)", expected);
});
await Check("live-punch-without-state-refresh", async (page, _) =>
{
    var job = Raw(new[] { Asm("*71 1002"), Asm("uj 1001"), (1UL << 39) | (515UL << 24) | 538UL }
        .Concat(Enumerable.Range(0, 24).Select(i => (ulong)i * 7)));
    await Upload(page, job); await page.CheckAsync("input[value=full]");
    await page.FillAsync("#instructionLimit", "1000000000");
    await page.ClickAsync("#run"); await Status(page, "Running");
    await page.WaitForFunctionAsync("document.getElementById('punchOutput').textContent.length>0");
    if (await page.Locator("#status").TextContentAsync() != "Running") throw new Exception("Punch output arrived only at completion");
    if (await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='registers'||e.type==='memory').length") != 0)
        throw new Exception("Device polling includes machine state");
    await page.ClickAsync("#stop"); await Status(page, "Halted");
});
foreach (string type in new[] { "watanabe", "tektronix", "calcomp" })
    await Check("plotter-" + type, async (page, _) =>
    {
        string tekPoint(int x, int y) => new(new[] { (char)(32 | (y >> 5)), (char)(96 | (y & 31)), (char)(32 | (x >> 5)), (char)(64 | (x & 31)) });
        string data = type == "watanabe" ? "M0,0\nD100,0\nD100,100\n" : "\u001d" + tekPoint(0, 0) + tekPoint(100, 100) + tekPoint(200, 0);
        string job = type == "calcomp"
            ? Raw([Asm("xta 1003, *57 2"), Asm("xta 1004, *57 2"), Asm("stop"), 10UL, 18UL])
            : Raw(new[] { Asm("xta 1002, *61 77777"), Asm("stop"), 515UL | (type == "tektronix" ? 0x300UL << 36 : 0UL) }.Concat(Bytes(data)));
        await Upload(page, job, type + ".dub"); await page.CheckAsync("input[value=full]");
        await page.ClickAsync("#run"); await Status(page, "Completed"); await page.ClickAsync("#plotterTab");
        await page.Locator("#plotterPane.show").WaitForAsync();
        await page.WaitForFunctionAsync("document.getElementById('plotterCanvas').dataset.segments === '2'");
        if (await page.Locator("#plotterType").InputValueAsync() != type) throw new Exception("Wrong plotter type selected");
        bool drawing = await page.EvaluateAsync<bool>("(()=>{const c=document.getElementById('plotterCanvas');const p=c.getContext('2d').getImageData(0,0,c.width,c.height).data;return p.some((v,i)=>i%4!==3&&v<100)})()");
        if (!drawing) throw new Exception("Canvas is blank");
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "plotter-" + type + ".png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
    });
foreach (string name in new[] { "watanabe", "calcomp", "tektronix", "cardioide" })
    await Check("grafor-" + name, async (page, _) =>
    {
        await Upload(page, File.ReadAllText("examples/grafor/" + name + ".dub"), name + ".dub");
        await page.CheckAsync("input[value=full]");
        await page.FillAsync("#instructionLimit", "100000000");
        await page.ClickAsync("#run");
        await page.WaitForFunctionAsync("['Completed','Failed'].includes(document.getElementById('status').textContent)", null, new() { Timeout = 60000 });
        await File.WriteAllTextAsync(Path.Combine(output, "grafor-" + name + ".txt"), await GuestOutput(page));
        await page.ClickAsync("#plotterTab");
        await page.Locator("#plotterPane.show").WaitForAsync();
        Console.WriteLine("GRAFOR " + name + " " + await page.EvaluateAsync<string>("JSON.stringify({status:document.getElementById('status').textContent,bytes:document.getElementById('plotterInfo').textContent,segments:document.getElementById('plotterCanvas').dataset.segments,raw:document.getElementById('plotterRaw').textContent.slice(0,300)})"));
        if (await page.Locator("#status").TextContentAsync() != "Completed") throw new Exception("GRAFOR job failed");
        await page.WaitForFunctionAsync("Number(document.getElementById('plotterCanvas').dataset.segments)>20");
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "grafor-" + name + ".png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
    });
await Check("libpunch", async (page, _) =>
{
    await Upload(page, File.ReadAllText("examples/libpunch.dub"), "libpunch.dub");
    await page.CheckAsync("input[value=full]");
    await page.FillAsync("#instructionLimit", "100000000");
    await page.ClickAsync("#run"); await Status(page, "Completed");
    await page.ClickAsync("#punchTab");
    await page.Locator("#punchPane.show").WaitForAsync();
    string cards = await page.Locator("#punchOutput").TextContentAsync() ?? "";
    if (!cards.Any(c => c > 0x2800 && c <= 0x28ff)) throw new Exception("LIBPUNCH produced no punched holes");
    var rows = cards.Split('\n').Where(line => line.Length > 0).ToArray();
    if (rows.Length % 3 != 0 || rows.Any(row => row.Length != 40)) throw new Exception("Invalid punch card dimensions");
    string id = await page.EvaluateAsync<string>("window.__sessionId");
    await using var stream = new FileStream(Path.Combine(Path.GetTempPath(), "besm6-web", id, "punch.out"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    string diskOutput = await reader.ReadToEndAsync();
    if (cards != diskOutput) throw new Exception("Punch output differs from device file");
    Console.WriteLine("LIBPUNCH cards: " + rows.Length / 3);
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "libpunch.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
});
await Check("terminal-and-storage", async (page, _) =>
{
    string job = Raw(new[] { Asm("*71 1002"), Asm("stop"), (4UL << 39) | (515UL << 24) }.Concat(Bytes("HI")));
    await Upload(page, job); await page.CheckAsync("input[value=full]");
    await page.ClickAsync("#run"); await Status(page, "Completed");
    await page.ClickAsync("#terminalTab");
    if ((await page.Locator("#terminalOutput").InnerTextAsync()).Trim() != "HI") throw new Exception("Terminal has no output");
    if (!string.IsNullOrEmpty(await page.Locator("#printOutput").InnerTextAsync())) throw new Exception("Terminal output also went to printer");
    await page.ClickAsync("#storageTab");
    if (!(await page.Locator("#storageMounts").InnerTextAsync()).Contains("Drum") || !(await page.Locator("#storageFiles").InnerTextAsync()).Contains("job.dub"))
        throw new Exception("Missing mounted devices or session files");
});
await Check("punch-device", async (page, _) =>
{
    var job = Raw(new[] { Asm("*71 1002"), Asm("stop"), (1UL << 39) | (515UL << 24) | 538UL }.Concat(Enumerable.Range(0,24).Select(i=>(ulong)i*7)));
    await Upload(page, job); await page.CheckAsync("input[value=full]");
    string? previous = null;
    for (int run = 0; run < 2; run++)
    {
        await page.ClickAsync("#run"); await Status(page, "Completed"); await page.ClickAsync("#punchTab");
        string text = await page.Locator("#punchOutput").InnerTextAsync();
        if (text.Length == 0 || (previous is not null && previous != text)) throw new Exception("Punch output is missing or stale");
        previous = text;
    }
});
await Check("upload-validation", async (page, _) =>
{
    foreach (var (name, content) in new[] { ("bad.txt", new byte[] { 1 }), ("empty.dub", Array.Empty<byte>()), ("too-large.dub", new byte[10_000_001]) })
    {
        await page.Locator("#file").SetInputFilesAsync(new FilePayload { Name = name, MimeType = "text/plain", Buffer = content });
        await page.WaitForFunctionAsync("document.getElementById('fileName').textContent === 'No valid job selected'");
        if (await page.Locator("#run").IsEnabledAsync()) throw new Exception("Invalid upload can be run: " + name);
    }
    await Upload(page); await page.ClickAsync("#run"); await Status(page, "Paused");
});
await Check("statistics-checkbox", async (page, _) =>
{
    await Upload(page); await page.CheckAsync("input[value=full]"); await page.CheckAsync("#statistics");
    await page.ClickAsync("#run"); await Status(page, "Completed");
    await page.Locator("#statisticsPanel").WaitForAsync();
    if (!(await page.Locator("#statisticsOutput").InnerTextAsync()).Contains("MIPS")) throw new Exception("Missing statistics report");
    if (await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='statistics').length") != 1) throw new Exception("Statistics event missing");
    await page.UncheckAsync("#statistics"); await page.ClickAsync("#run"); await Status(page, "Completed");
    if (await page.Locator("#statisticsPanel").IsVisibleAsync()) throw new Exception("Statistics remain visible with collection off");
    if (await page.EvaluateAsync<int>("window.__events.filter(e=>e.type==='statistics').length") != 1) throw new Exception("Statistics collected with checkbox off");
});
await Check("reset-button", async (page, _) =>
{
    await Upload(page, loop); await page.CheckAsync("input[value=full]"); await page.FillAsync("#instructionLimit", "1000000000");
    await page.ClickAsync("#run"); await Status(page, "Running"); await page.ClickAsync("#reset"); await Status(page, "Idle");
    if (await page.Locator("#instructionCount").TextContentAsync() != "0 instructions") throw new Exception("Reset kept instruction count");
    if (await page.Locator("#run").IsDisabledAsync()) throw new Exception("Reset lost uploaded job");
    if (await page.Locator("#fileName").TextContentAsync() != "smoke.dub") throw new Exception("Reset lost filename");
    if (!(await page.Locator("#memory").TextContentAsync())!.Split('\n').All(line=>line.Split(' ',StringSplitOptions.RemoveEmptyEntries)[1]=="0000000000000000")) throw new Exception("Reset kept memory");
    await page.CheckAsync("input[value=debug]"); await page.ClickAsync("#run"); await Status(page, "Paused");
    await page.ClickAsync("#reset"); await Status(page, "Idle");
});
await Check("number-bases", async (page, _) =>
{
    const ulong value = 0xFEDCBA987654;
    await Upload(page, Raw([Asm("xta 1002"), Asm("stop"), value])); await page.ClickAsync("#run"); await Status(page, "Paused");
    await page.ClickAsync("#step"); await Status(page, "Paused");
    await page.FillAsync("#address", "1002"); await page.ClickAsync("#inspect");
    await page.WaitForFunctionAsync("document.getElementById('memory').textContent.startsWith('01002')");
    foreach (int radix in new[] { 8, 10, 16, 8 })
    {
        await page.SelectOptionAsync("#numberBase", radix.ToString());
        string formatted = Convert.ToString((long)value, radix).ToUpperInvariant();
        if (radix != 10) formatted = formatted.PadLeft(radix == 8 ? 16 : 12, '0');
        string register = await page.EvaluateAsync<string>("[...document.querySelectorAll('#registers .register')].find(e=>e.querySelector('dt').textContent==='A').querySelector('dd').textContent");
        if (register != formatted) throw new Exception("48-bit value lost precision in radix " + radix);
        if (await page.Locator("#address").InputValueAsync() != Convert.ToString(514, radix).ToUpperInvariant()) throw new Exception("Address changed while switching radix");
        if (!(await page.Locator("#memory").TextContentAsync())!.Split('\n')[0].Split(' ',StringSplitOptions.RemoveEmptyEntries)[1].Equals(formatted)) throw new Exception("Memory radix mismatch");
        await page.ClickAsync("#inspect");
    }
    await page.SelectOptionAsync("#numberBase", "16"); await page.FillAsync("#address", "8000"); await page.ClickAsync("#inspect");
    await page.WaitForFunctionAsync("document.getElementById('error').textContent.includes('7FFF')");
});
await Check("braille-font", async (page, _) =>
{
    await page.ClickAsync("#punchTab"); await page.Locator("#punchPane.show").WaitForAsync();
    var metrics = await page.EvaluateAsync<JsonElement>("""
    async()=>{await document.fonts.load('20px "BESM6 Braille"','\u2800\u28ff');const c=document.createElement('canvas').getContext('2d');c.font='20px "BESM6 Braille"';const widths=Array.from({length:256},(_,i)=>c.measureText(String.fromCharCode(0x2800+i)).width);return {loaded:document.fonts.check('20px "BESM6 Braille"','\u2800\u28ff'),min:Math.min(...widths),max:Math.max(...widths)}}
    """);
    if (!metrics.GetProperty("loaded").GetBoolean() || Math.Abs(metrics.GetProperty("max").GetDouble()-metrics.GetProperty("min").GetDouble()) > .01) throw new Exception("Braille cells have different widths: " + metrics);
    await page.EvaluateAsync("document.getElementById('punchOutput').textContent=Array.from({length:256},(_,i)=>String.fromCharCode(0x2800+i)+(i%40===39?'\\n':'')).join('')");
    await page.ScreenshotAsync(new() { Path=Path.Combine(output,"braille-font.png"), FullPage=true });
});
await Check("card-generator", async (page, _) =>
{
    await page.ClickAsync("#cardTab"); await page.Locator("#cardPane.show").WaitForAsync();
    await page.WaitForFunctionAsync("!document.getElementById('cardText').disabled");
    foreach (var encoding in new[] { "ibm", "upp" })
    {
        await page.SelectOptionAsync("#cardEncoding", encoding);
        const string text = "ABCДЖ⑩◊¬␣";
        await page.FillAsync("#cardText", text);
        var expected = Besm6.Runtime.Devices.PunchCardCodec.Encode(text, encoding == "ibm" ? Besm6.Runtime.Devices.PunchCardEncoding.Ibm : Besm6.Runtime.Devices.PunchCardEncoding.Upp);
        var actual = await page.EvaluateAsync<int[]>("[...document.querySelectorAll('.card-cell[aria-pressed=true]')].map(e=>Number(e.dataset.index))");
        var wanted = Enumerable.Range(0,960).Where(i=>expected.IsPunched(i/80,i%80)).ToArray();
        if (!actual.SequenceEqual(wanted)) throw new Exception("Generator differs from runtime codec: " + encoding);
    }
    await page.SelectOptionAsync("#cardEncoding", "ibm"); await page.ClickAsync("#cardClear");
    await page.Locator(".card-cell[data-index='0']").ClickAsync();
    if (await page.Locator("#cardText").InputValueAsync() != "×") throw new Exception("Click did not decode IBM column");
    await page.ClickAsync("#cardClear");
    foreach (int index in new[] {160,720,800}) await page.Locator($".card-cell[data-index='{index}']").ClickAsync();
    if (await page.Locator("#cardText").InputValueAsync() != "⑩") throw new Exception("Decimal exponent column is wrong");
    await page.SelectOptionAsync("#cardEncoding", "upp"); await page.ClickAsync("#cardClear");
    await page.Locator(".card-cell[data-index='2']").ClickAsync();
    if (await page.Locator("#cardText").InputValueAsync() != "A") throw new Exception("Click did not decode UPP byte");
    await page.Locator(".card-cell[data-index='7']").ClickAsync();
    if (!(await page.Locator("#cardMessage").TextContentAsync())!.Contains("parity", StringComparison.OrdinalIgnoreCase)) throw new Exception("Parity errors are not detected");
    await page.ClickAsync("#cardClear"); await page.SelectOptionAsync("#cardEncoding", "ibm"); await page.FillAsync("#cardText", "A");
    var dimensions = await page.EvaluateAsync<JsonElement>("""
    (()=>{const s=document.querySelector('#cardPreview svg'), h=s.querySelector('[data-holes] rect'), n=[...s.querySelectorAll('text')].filter(e=>e.textContent==='80');return {width:s.getAttribute('width'),height:s.getAttribute('height'),viewBox:s.getAttribute('viewBox'),cut:s.querySelector('polygon').getAttribute('points'),x:h.getAttribute('x'),y:h.getAttribute('y'),hw:h.getAttribute('width'),hh:h.getAttribute('height'),cells:s.querySelectorAll('.card-cell').length,columnY:n[0].getAttribute('y')}})()
    """);
    if (dimensions.GetProperty("width").GetString() != "187.325mm" || dimensions.GetProperty("height").GetString() != "82.55mm" || dimensions.GetProperty("cells").GetInt32()!=960
        || dimensions.GetProperty("x").GetString()!="4.5466" || dimensions.GetProperty("y").GetString()!="4.7625" || dimensions.GetProperty("hw").GetString()!="1.397" || dimensions.GetProperty("hh").GetString()!="3.175") throw new Exception("Incorrect physical geometry: " + dimensions);
    await page.FillAsync("#cardText", "      A=B+3");
    var svgTask = page.WaitForDownloadAsync(); await page.ClickAsync("#cardSvg"); var svgFile=await svgTask;
    string svgPath=Path.Combine(output,"generated-card.svg"); await svgFile.SaveAsAsync(svgPath);
    string svg=await File.ReadAllTextAsync(svgPath);
    if (svg.Contains("data-editor") || !svg.Contains("187.325mm")) throw new Exception("Editor controls leaked into exported SVG");
    var pngTask=page.WaitForDownloadAsync(); await page.ClickAsync("#cardPng"); var pngFile=await pngTask;
    string pngPath=Path.Combine(output,"generated-card.png"); await pngFile.SaveAsAsync(pngPath);
    byte[] png=await File.ReadAllBytesAsync(pngPath);
    uint BigEndian(int at)=>System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at,4));
    if (BigEndian(16)!=4425 || BigEndian(20)!=1950) throw new Exception("PNG is not 600 dpi size");
    int densityCount=0;
    for (int offset=8; offset<png.Length; offset+=checked((int)BigEndian(offset)+12))
        if (Encoding.ASCII.GetString(png,offset+4,4)=="pHYs") { densityCount++; if (BigEndian(offset+8)!=23622 || BigEndian(offset+12)!=23622) throw new Exception("Wrong PNG density"); }
    if (densityCount!=1) throw new Exception("Missing or duplicate PNG density metadata");
    await page.ScreenshotAsync(new() { Path=Path.Combine(output,"card-generator.png"), FullPage=true, Animations=ScreenshotAnimations.Disabled });
});
await Check("cli-performance-parity", async (page, _) =>
{
    string configuration=Environment.GetEnvironmentVariable("BESM6_WEB_BUILD_CONFIGURATION") ?? "Debug";
    string cli=Path.GetFullPath($"src/Besm6.Cli/bin/{configuration}/net8.0/besm6.dll");
    if (!File.Exists(cli)) throw new Exception("Build the CLI in the same configuration: " + configuration);
    var comparisons=new List<object>();
    foreach (var (name, text) in new[] { ("loop",loop), ("fortran",File.ReadAllText("examples/fortran.dub")) })
    {
        string directory=Path.Combine(output,"cli-parity-"+name+"-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string job=Path.Combine(directory,"job.dub"); await File.WriteAllTextAsync(job,text);
        var start=new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory=directory, RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false, CreateNoWindow=true };
        foreach(string arg in new[] {cli,"run",job,"--limit","10000000","--stats","--speed","max"}) start.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(start)!;
        var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); string outputText=await stdout; await stderr;
        string json=outputText.Split('\n').Single(line=>line.StartsWith("Execution stats: "))[17..];
        var consoleStatistics=JsonDocument.Parse(json).RootElement.GetProperty("statistics").Clone();
        await Upload(page,text,name+".dub"); await page.CheckAsync("input[value=full]"); await page.CheckAsync("#statistics"); await page.UncheckAsync("#autoRefresh");
        await page.FillAsync("#instructionLimit","10000000"); await page.ClickAsync("#run");
        await page.WaitForFunctionAsync("['Completed','Failed'].includes(document.getElementById('status').textContent)");
        var web=await page.EvaluateAsync<JsonElement>("window.__events.findLast(e=>e.type==='statistics').data");
        if (web.GetProperty("completedInstructions").GetInt64()!=consoleStatistics.GetProperty("completedInstructions").GetInt64()
            || web.GetProperty("modelCycles").GetInt64()!=consoleStatistics.GetProperty("modelCycles").GetInt64()) throw new Exception("CLI and web executed different workloads");
        double cliSeconds=consoleStatistics.GetProperty("elapsedSeconds").GetDouble(), webSeconds=web.GetProperty("elapsedSeconds").GetDouble();
        var sample=new {name,configuration,cliSeconds,webSeconds,ratio=webSeconds/cliSeconds,instructions=web.GetProperty("completedInstructions").GetInt64()};
        comparisons.Add(sample); Console.WriteLine("CLI PARITY "+JsonSerializer.Serialize(sample));
        await page.ClickAsync("#reset"); await Status(page,"Idle");
    }
    await File.WriteAllTextAsync(Path.Combine(output,"cli-performance-parity.json"),JsonSerializer.Serialize(comparisons,new JsonSerializerOptions {WriteIndented=true}));
});
await Check("dhrystone-modes", async (page, _) =>
{
    page.SetDefaultTimeout(120000);
    string configuration=Environment.GetEnvironmentVariable("BESM6_WEB_BUILD_CONFIGURATION") ?? "Release";
    var samples=new List<object>(); var webMax=new List<double>(); var cliMax=new List<double>();
    long? expectedInstructions=null; long? expectedCycles=null;
    async Task Run(string preset, bool refresh=false, bool decode=false)
    {
        await page.ClickAsync("#examplesTab");
        await page.WaitForFunctionAsync("document.querySelector('#exampleSelect option[value=\"web-jobs/dhrystone-max.json\"]')");
        await page.SelectOptionAsync("#exampleSelect", "web-jobs/dhrystone-"+preset+".json");
        await page.ClickAsync("#exampleLoad");
        await page.WaitForFunctionAsync("name=>document.getElementById('fileName').textContent===name", "web-jobs/dhrystone-"+preset+".json");
        if(await page.Locator("#machineModel").InputValueAsync()!="dubna" || await page.Locator("#autoRefresh").IsCheckedAsync()) throw new Exception("Preset settings were not applied");
        if(refresh) await page.CheckAsync("#autoRefresh");
        if(decode) await page.CheckAsync("#decodeMemory");
        await page.ClickAsync("#run");
        if(preset=="debug") { await Status(page,"Paused"); await page.ClickAsync("#resume"); }
        await Status(page,"Completed");
        await page.WaitForFunctionAsync("/BAD CONTROL VALUES=\\s+0\\s+ALL 18 CONTROL VALUES MATCH/.test(document.getElementById('printOutput').textContent) && /GUEST DMIPS:\\s+\\d+\\.\\d+/.test(document.getElementById('printOutput').textContent)");
        string text=await GuestOutput(page);
        string resultText=text[text.LastIndexOf("RUNNING     5000 PASSES",StringComparison.Ordinal)..];
        if(!resultText.Contains("ALL 18 CONTROL VALUES MATCH") || !resultText.Contains("ALL 4 STRING VALUES MATCH: YES") || !resultText.Contains("GUEST DMIPS:") || resultText.Contains("DMIPS UNAVAILABLE")) throw new Exception("Dhrystone controls or timer failed: "+resultText);
        var statistics=await page.EvaluateAsync<JsonElement>("window.__events.findLast(e=>e.type==='statistics').data");
        long instructions=statistics.GetProperty("completedInstructions").GetInt64(), cycles=statistics.GetProperty("modelCycles").GetInt64();
        // Original uses the statistical guest clock; formatting different elapsed times can execute different instructions.
        // Full/Debug/refresh comparisons use the same max clock and must keep identical work.
        if(preset is "max" or "debug") {
            expectedInstructions ??= instructions; expectedCycles ??= cycles;
            if(instructions!=expectedInstructions || cycles!=expectedCycles) throw new Exception("Mode changed Dhrystone workload");
        }
        double seconds=statistics.GetProperty("elapsedSeconds").GetDouble();
        if(preset=="max" && !refresh) webMax.Add(seconds);
        samples.Add(new{preset,refresh,decode,statistics}); Console.WriteLine($"DHRYSTONE {preset} refresh={refresh} decode={decode}: {seconds:F4}s / {instructions} instructions");
        await File.WriteAllTextAsync(Path.Combine(output,$"dhrystone-{preset}-{refresh}-{decode}.txt"),text);
        await page.ClickAsync("#reset"); await Status(page,"Idle");
    }
    await Run("max"); await Run("original"); await Run("virtual"); await Run("debug");
    await Run("max",true); await Run("max",true,true); await Run("max"); await Run("max");
    string cli=Path.GetFullPath($"src/Besm6.Cli/bin/{configuration}/net8.0/besm6.dll");
    for(int repeat=0;repeat<3;repeat++) {
        string directory=Path.Combine(output,"dhrystone-cli-"+repeat); Directory.CreateDirectory(directory);
        var start=new System.Diagnostics.ProcessStartInfo("dotnet"){WorkingDirectory=directory,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        foreach(string arg in new[]{cli,"run",Path.GetFullPath("examples/dhrystone-forex.dub"),"--speed","max","--guest-clock","--stats","--limit","1000000000"}) start.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(start)!; var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); string text=await stdout; string diagnostic=await stderr;
        await File.WriteAllTextAsync(Path.Combine(directory,"output.txt"),text+diagnostic);
        if(process.ExitCode!=0 || !text.Contains("ALL 18 CONTROL VALUES MATCH")) throw new Exception("CLI Dhrystone failed");
        var statistics=JsonDocument.Parse(text.Split('\n').Single(line=>line.StartsWith("Execution stats: "))[17..]).RootElement.GetProperty("statistics").Clone();
        if(statistics.GetProperty("completedInstructions").GetInt64()!=expectedInstructions || statistics.GetProperty("modelCycles").GetInt64()!=expectedCycles) throw new Exception("CLI/web workload mismatch");
        cliMax.Add(statistics.GetProperty("elapsedSeconds").GetDouble()); samples.Add(new{cliRepeat=repeat,statistics});
    }
    double webMedian=webMax.Order().ElementAt(1), cliMedian=cliMax.Order().ElementAt(1);
    await File.WriteAllTextAsync(Path.Combine(output,"dhrystone-modes.json"),JsonSerializer.Serialize(new{configuration,webMedian,cliMedian,ratio=webMedian/cliMedian,samples},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"DHRYSTONE PARITY web={webMedian:F4}s cli={cliMedian:F4}s ratio={webMedian/cliMedian:F3}");
    if(webMedian>cliMedian*1.15+0.15) throw new Exception("Dhrystone web slowdown exceeds the performance budget");
    await Upload(page,File.ReadAllText("examples/dhrystone-forex.dub")); await page.SelectOptionAsync("#machineModel","buffered");
    await page.ClickAsync("#run"); await Status(page,"Completed");
    await page.WaitForFunctionAsync("/BAD CONTROL VALUES=\\s+0\\s+ALL 18 CONTROL VALUES MATCH/.test(document.getElementById('printOutput').textContent)");
    var buffered=await page.EvaluateAsync<JsonElement>("window.__events.findLast(e=>e.type==='statistics').data");
    if(buffered.GetProperty("completedInstructions").GetInt64()!=expectedInstructions || buffered.GetProperty("modelCycles").GetInt64()!=expectedCycles) throw new Exception("Buffered Dhrystone workload changed");
    await File.WriteAllTextAsync(Path.Combine(output,"dhrystone-buffered.json"),buffered.ToString());
});
await Check("dhrystone-buffered", async (page, _) =>
{
    page.SetDefaultTimeout(120000);
    await page.ClickAsync("#examplesTab");
    await page.WaitForFunctionAsync("document.querySelector('#exampleSelect option[value=\"web-jobs/dhrystone-buffered.json\"]')");
    await page.SelectOptionAsync("#exampleSelect","web-jobs/dhrystone-buffered.json"); await page.ClickAsync("#exampleLoad");
    await page.WaitForFunctionAsync("document.getElementById('fileName').textContent==='web-jobs/dhrystone-buffered.json' && !document.getElementById('run').disabled");
    if(await page.Locator("#machineModel").InputValueAsync()!="buffered") throw new Exception("Buffered preset did not select its model");
    await page.ClickAsync("#run"); await Status(page,"Completed");
    await page.WaitForFunctionAsync("/BAD CONTROL VALUES=\\s+0\\s+ALL 18 CONTROL VALUES MATCH/.test(document.getElementById('printOutput').textContent) && /GUEST DMIPS:\\s+\\d+\\.\\d+/.test(document.getElementById('printOutput').textContent)");
    var statistics=await page.EvaluateAsync<JsonElement>("window.__events.findLast(e=>e.type==='statistics').data");
    string text=await GuestOutput(page);
    if(!text.Contains("ALL 4 STRING VALUES MATCH: YES") || statistics.GetProperty("completedInstructions").GetInt64()!=24606703 || statistics.GetProperty("modelCycles").GetInt64()!=40894857) throw new Exception("Buffered Dhrystone differs from the verified ordinary Dubna run");
    await File.WriteAllTextAsync(Path.Combine(output,"dhrystone-buffered.json"),statistics.ToString());
    await File.WriteAllTextAsync(Path.Combine(output,"dhrystone-buffered.txt"),text);
    Console.WriteLine("DHRYSTONE BUFFERED "+statistics);
});
await Check("json-preset-upload", async (page, _) =>
{
    await page.SelectOptionAsync("#machineModel","supervisor-hardware");
    await Upload(page,File.ReadAllText("examples/web-jobs/dhrystone-max.json"),"dhrystone-max.json");
    await page.WaitForFunctionAsync("document.getElementById('machineModel').value==='dubna' && document.getElementById('statistics').checked");
    if(await page.Locator("#autoRefresh").IsCheckedAsync() || await page.Locator("#decodeMemory").IsCheckedAsync()) throw new Exception("File preset did not disable inspection");
    await page.ClickAsync("#editorTab"); await page.FillAsync("#sourceText",File.ReadAllText("examples/web-jobs/dhrystone-virtual.json"));
    await page.ClickAsync("#sourceLoad");
    await page.WaitForFunctionAsync("document.getElementById('fileName').textContent==='editor.json' && document.getElementById('speed').value==='virtual'");
    string session=await page.EvaluateAsync<string>("window.__sessionId");
    byte[] actual=await File.ReadAllBytesAsync(Path.Combine(Path.GetTempPath(),"besm6-web",session,"job.dub"));
    if(!actual.SequenceEqual(await File.ReadAllBytesAsync("examples/dhrystone-forex.dub"))) throw new Exception("JSON preset changed the benchmark source");
    if(await page.Locator("#status").TextContentAsync()!="Idle") throw new Exception("Loading a preset started execution");
});
await Check("json-machine-examples", async (page, _) =>
{
    foreach(string model in new[]{"functional","hardware"}) {
        await page.ClickAsync("#examplesTab"); await page.WaitForFunctionAsync("document.getElementById('exampleSelect').options.length>100");
        await page.SelectOptionAsync("#exampleSelect","machines/supervisor-"+model+".json"); await page.ClickAsync("#exampleLoad");
        await page.WaitForFunctionAsync("name=>document.getElementById('fileName').textContent===name","machines/supervisor-"+model+".json");
        if(await page.Locator("#machineModel").InputValueAsync()!="supervisor-"+model) throw new Exception("Machine JSON did not select its execution model");
        await page.CheckAsync("input[value=full]"); await page.ClickAsync("#run"); await Status(page,"Completed");
        if(!string.IsNullOrEmpty(await page.Locator("#error").TextContentAsync())) throw new Exception("Ready machine JSON failed");
        await page.ClickAsync("#reset"); await Status(page,"Idle");
    }
});
await Check("examples-browser", async (page, _) =>
{
    await page.SelectOptionAsync("#machineModel", "supervisor-hardware");
    await page.ClickAsync("#examplesTab"); await page.Locator("#examplesPane.show").WaitForAsync();
    await page.WaitForFunctionAsync("document.getElementById('exampleSelect').options.length>100");
    await page.FillAsync("#exampleFilter","fortran");
    await page.SelectOptionAsync("#exampleSelect","fortran.dub");
    await page.WaitForFunctionAsync("document.getElementById('exampleSource').textContent.includes('Hello, World!')");
    await page.ClickAsync("#exampleLoad");
    await page.WaitForFunctionAsync("document.getElementById('fileName').textContent==='fortran.dub'");
    await page.WaitForFunctionAsync("document.getElementById('machineModel').value==='dubna'");
    if (await page.Locator("#status").TextContentAsync()!="Idle") throw new Exception("Loading an example executed it");
    await page.SelectOptionAsync("#machineModel", "supervisor-functional");
    await page.CheckAsync("input[value=full]"); await page.ClickAsync("#exampleRun"); await Status(page,"Completed");
    if (!(await GuestOutput(page)).Contains("HELLO, WORLD!",StringComparison.OrdinalIgnoreCase)) throw new Exception("Selected example did not run");
    string id=await page.EvaluateAsync<string>("window.__sessionId");
    byte[] saved=await File.ReadAllBytesAsync(Path.Combine(Path.GetTempPath(),"besm6-web",id,"job.dub"));
    if (!saved.SequenceEqual(await File.ReadAllBytesAsync("examples/fortran.dub"))) throw new Exception("Example contents changed during load");
    await page.ScreenshotAsync(new() {Path=Path.Combine(output,"examples-browser.png"),FullPage=true,Animations=ScreenshotAnimations.Disabled});
    await page.ClickAsync("#reset"); await Status(page,"Idle");
    await page.FillAsync("#exampleFilter","grafor/watanabe"); await page.SelectOptionAsync("#exampleSelect","grafor/watanabe.dub");
    await page.WaitForFunctionAsync("document.getElementById('exampleSource').textContent.includes('call symbol')");
    await page.CheckAsync("input[value=debug]"); await page.ClickAsync("#exampleRun"); await Status(page,"Paused");
    if (await page.Locator("#exampleLoad").IsEnabledAsync() || await page.Locator("#exampleRun").IsEnabledAsync()) throw new Exception("Examples can replace an active job");
    await page.FillAsync("#exampleFilter","hello");
    if (await page.Locator("#status").TextContentAsync()!="Paused") throw new Exception("Browsing examples changed machine state");
    var invalid=await page.APIRequest.GetAsync(root+"/api/examples/source?id="+Uri.EscapeDataString("../besm6.json"));
    if (invalid.Status!=404) throw new Exception("Example endpoint accepts a path outside the catalog");
});
await Check("source-editor", async (page, _) =>
{
    await page.ClickAsync("#examplesTab");
    await page.WaitForFunctionAsync("document.getElementById('exampleSelect').options.length>100");
    await page.SelectOptionAsync("#exampleSelect", "fortran.dub");
    await page.WaitForFunctionAsync("document.querySelector('#exampleSource .syntax-keyword') && !document.getElementById('exampleEdit').disabled");
    string original = await page.Locator("#exampleSource").TextContentAsync() ?? "";
    if (original != await File.ReadAllTextAsync("examples/fortran.dub")) throw new Exception("Highlighting changed source text");
    await page.ClickAsync("#exampleEdit"); await page.Locator("#editorPane.show").WaitForAsync();
    string normalized = original.Replace("\r\n", "\n");
    if (await page.Locator("#sourceText").InputValueAsync() != normalized) throw new Exception("Edit copy changed source");
    string edited = normalized.Replace("Hello, World!", "EDITOR OK");
    await page.FillAsync("#sourceText", edited); await page.CheckAsync("input[value=full]"); await page.ClickAsync("#sourceRun"); await Status(page, "Completed");
    await page.WaitForFunctionAsync("document.getElementById('printOutput').textContent.includes('EDITOR OK')");
    string session = await page.EvaluateAsync<string>("window.__sessionId");
    if (await File.ReadAllTextAsync(Path.Combine(Path.GetTempPath(),"besm6-web",session,"job.dub")) != edited) throw new Exception("Editor text did not reach the machine");
    await page.ScreenshotAsync(new() { Path=Path.Combine(output,"source-editor.png"),FullPage=true });
    string hostile = "*fortran\n      print *, '<img src=x onerror=window.__sourceXss=1>'\n*end file\n";
    await page.FillAsync("#sourceText", hostile);
    if (await page.Locator("#sourcePreview").TextContentAsync() != hostile || await page.Locator("#sourcePreview img").CountAsync() != 0
        || await page.EvaluateAsync<bool>("!!window.__sourceXss")) throw new Exception("Source text became HTML");
    await page.FillAsync("#sourceText", loop); await page.FillAsync("#instructionLimit","1000000000"); await page.ClickAsync("#sourceRun"); await Status(page,"Running");
    await page.FillAsync("#sourceText", tiny);
    if (await page.Locator("#sourceRun").IsEnabledAsync() || await page.Locator("#status").TextContentAsync() != "Running") throw new Exception("Editing replaced an active job");
    await page.ClickAsync("#stop"); await Status(page,"Halted");
});
await Check("terminal-input", async (page, _) =>
{
    string job = Raw([Asm("*71 1003"),Asm("*71 1004"),Asm("stop"),(6UL<<39)|(517UL<<24)|517UL,(4UL<<39)|(517UL<<24)|517UL,0UL]);
    await Upload(page, job); await page.CheckAsync("input[value=full]");
    foreach (string target in new[] { "terminal", "teletype" })
    {
        await page.ClickAsync("#run"); await Status(page,"WaitingInput");
        await page.Locator("#terminalPane.show").WaitForAsync();
        if (await page.Locator("#run").IsEnabledAsync() || await page.Locator("#stop").IsDisabledAsync()) throw new Exception("Invalid controls while waiting for input");
        await page.SelectOptionAsync("#terminalTarget", target); await page.FillAsync("#terminalInput","AB"); await page.ClickAsync("#terminalSend");
        await Status(page,"Completed"); await page.WaitForFunctionAsync("document.getElementById('terminalOutput').textContent.includes('AB')");
        await page.ClickAsync("#reset"); await Status(page,"Idle");
    }
    await page.ClickAsync("#run"); await Status(page,"WaitingInput"); await page.ClickAsync("#reset"); await Status(page,"Idle");
    await page.SelectOptionAsync("#terminalTarget","terminal"); await page.FillAsync("#terminalInput","PREQUEUED"); await page.ClickAsync("#terminalSend");
    await page.WaitForFunctionAsync("window.__events.some(e=>e.type==='inputAccepted')");
    await page.ClickAsync("#run"); await Status(page,"Completed");
    if (!(await page.Locator("#terminalOutput").TextContentAsync())!.Contains("PREQUE")) throw new Exception("Prequeued input was lost");
    await page.ScreenshotAsync(new() { Path=Path.Combine(output,"terminal-input.png"),FullPage=true });
});
await Check("punch-formats", async (page, _) =>
{
    await Upload(page, await File.ReadAllTextAsync("examples/libpunch.dub")); await page.CheckAsync("input[value=full]");
    await page.ClickAsync("#run"); await Status(page,"Completed"); await page.ClickAsync("#punchTab");
    await page.WaitForFunctionAsync("document.getElementById('punchCount').textContent==='9 cards'");
    string braille = await page.Locator("#punchOutput").TextContentAsync() ?? "";
    foreach (string format in new[] { "ascii", "grid", "card", "braille" })
    {
        await page.SelectOptionAsync("#punchFormat",format);
        if (format == "card")
        {
            if (await page.Locator("#punchVisual svg").CountAsync() != 9 || await page.Locator("#punchVisual [data-holes] rect").CountAsync() == 0) throw new Exception("Physical cards are missing");
            await page.ScreenshotAsync(new() {Path=Path.Combine(output,"punch-formats.png"),FullPage=true});
        }
        else if (format == "braille") { if (await page.Locator("#punchOutput").TextContentAsync() != braille) throw new Exception("Switching formats changed Braille output"); }
        else
        {
            string text = await page.Locator("#punchOutput").TextContentAsync() ?? "";
            if (!text.Contains(format == "ascii" ? 'O' : '□') || (format == "ascii" && text.Any(c=>c>127))) throw new Exception("Wrong punch text format");
        }
    }
});
await Check("media-mount-and-disassembly", async (page, context) =>
{
    await page.ClickAsync("#storageTab"); await page.Locator("#storagePane.show").WaitForAsync();
    await page.WaitForFunctionAsync("document.querySelector('#mediaSource option[value=\"builtin:monsys.9\"]')");
    byte[] image = new byte[6144]; ulong word=Asm("xta 1002, stop");
    for (int i=0;i<6;i++) image[i]=(byte)(word>>(40-i*8));
    await page.Locator("#mediaFile").SetInputFilesAsync(new FilePayload {Name="test-disk.bin",MimeType="application/octet-stream",Buffer=image});
    await page.WaitForFunctionAsync("document.getElementById('mediaSource').value.startsWith('upload:')");
    string imageId=await page.Locator("#mediaSource").InputValueAsync();
    await page.SelectOptionAsync("#mediaKind","Tape"); await page.FillAsync("#mediaUnit","31"); await page.ClickAsync("#mediaMount");
    await page.WaitForFunctionAsync("document.getElementById('storageMounts').textContent.includes('Tape')");
    string job=Raw([Asm("*70 1002"),Asm("stop"),(1UL<<39)|(2UL<<30)|(25UL<<12)]);
    await Upload(page,job); await page.CheckAsync("input[value=full]"); await page.ClickAsync("#run"); await Status(page,"Completed");
    await page.FillAsync("#address","4000"); await page.ClickAsync("#inspect");
    string expected=Convert.ToString((long)word,8).PadLeft(16,'0');
    await page.WaitForFunctionAsync("expected=>document.getElementById('memory').textContent.split('\\n')[0].trim().split(/\\s+/)[1]===expected",expected);
    await page.SelectOptionAsync("#mediaSource","mounted:Tape:25"); await page.ClickAsync("#mediaRead");
    await page.WaitForFunctionAsync("document.getElementById('mediaDisassembly').textContent.includes('xta 1002,stop')");
    await page.FillAsync("#mediaAddress","3000"); await page.ClickAsync("#mediaLoad");
    await page.WaitForFunctionAsync("document.getElementById('mediaMessage').textContent.includes('loaded at 3000')");
    if (!(await page.Locator("#memory").TextContentAsync())!.Split('\n')[0].Split(' ',StringSplitOptions.RemoveEmptyEntries)[1].Equals(expected)) throw new Exception("Copy to memory lost media data");
    await page.SelectOptionAsync("#mediaSource",imageId); await page.ClickAsync("#mediaMount");
    await page.WaitForFunctionAsync("document.getElementById('error').textContent.includes('already mounted')");
    if (await page.Locator("#status").TextContentAsync()!="Completed") throw new Exception("Mount error changed machine state");
    await page.ScreenshotAsync(new() {Path=Path.Combine(output,"media-disassembly.png"),FullPage=true});
    var other=await Open(context); await other.ClickAsync("#storageTab");
    if (await other.Locator($"#mediaSource option[value='{imageId}']").CountAsync()!=0) throw new Exception("Uploaded image leaked across machines");
    await other.CloseAsync();
    await page.SelectOptionAsync("#mediaSource","builtin:monsys.9"); await page.ClickAsync("#mediaRead");
    await page.WaitForFunctionAsync("window.__events.some(e=>e.type==='mediaView'&&e.data.id==='builtin:monsys.9')");
    await page.ClickAsync("#mediaUnmount");
    await page.WaitForFunctionAsync("!document.getElementById('storageMounts').textContent.includes('Tape')");
    await Upload(page,await File.ReadAllTextAsync("examples/fortran.dub")); await page.ClickAsync("#run"); await Status(page,"Completed");
    if (!(await GuestOutput(page)).Contains("HELLO, WORLD!",StringComparison.OrdinalIgnoreCase)) throw new Exception("Media operations broke source execution");
});
await Check("instruction-codec", async (page, _) =>
{
    await page.ClickAsync("#instructionTab"); await page.Locator("#instructionPane.show").WaitForAsync();
    await page.WaitForFunctionAsync("document.querySelectorAll('#instructionBits button').length===24");
    // A distinct value per syntax makes the wait observe the completed decode,
    // rather than matching the previous result while its request is still pending.
    foreach(var (syntax,register) in new[]{("autocode",15),("bemsh",14),("madlen",13)})
    {
        var view=Besm6.Assembler.Disassembly.InstructionEditor.Decode(Besm6.Assembler.Disassembly.InstructionEditor.Encode(8,register,32767));
        string text=syntax=="autocode"?view.Autocode:syntax=="bemsh"?view.Bemsh:view.Madlen;
        await page.SelectOptionAsync("#instructionSyntax",syntax); await page.FillAsync("#instructionText",text); await page.ClickAsync("#instructionAssemble");
        await page.WaitForFunctionAsync("value=>document.getElementById('instructionDecoded').textContent.includes(value)",view.Hex);
    }
    await page.FillAsync("#instructionAddress","10000"); await page.ClickAsync("#instructionEncode");
    await page.WaitForFunctionAsync("document.getElementById('instructionError').textContent.length>0");
    await page.SelectOptionAsync("#instructionWidth","48"); await page.SelectOptionAsync("#instructionRadix","16");
    await page.FillAsync("#instructionValue","FFFFFFFFFFFF");
    await page.WaitForFunctionAsync("document.getElementById('instructionDecoded').textContent.includes('HEX FFFFFFFFFFFF')");
    if(await page.Locator("#instructionBits button").CountAsync()!=48) throw new Exception("Missing word halves");
    await page.Locator("#instructionBits button").First.ClickAsync();
    await page.WaitForFunctionAsync("document.getElementById('instructionDecoded').textContent.includes('HEX 7FFFFFFFFFFF')");
    await page.SelectOptionAsync("#instructionHalf","1"); await page.SelectOptionAsync("#instructionOpcode","8");
    await page.FillAsync("#instructionRegister","3"); await page.FillAsync("#instructionAddress","00010"); await page.ClickAsync("#instructionEncode");
    await page.WaitForFunctionAsync("document.getElementById('instructionDecoded').textContent.includes('HEX 7FFFFF308008')");
    await page.ScreenshotAsync(new(){Path=Path.Combine(output,"instruction-codec.png"),FullPage=true});
});
await Check("machine-models-and-registers", async (page, _) =>
{
    await Upload(page,Raw([Asm("xta 1004, atx 1005"),Asm("uj 1000"),0UL,0UL,0x123456789ABCUL]));
    await page.SelectOptionAsync("#machineModel","buffered"); await page.CheckAsync("input[value=full]"); await page.CheckAsync("#autoRefresh");
    await page.FillAsync("#instructionLimit","1000000000"); await page.ClickAsync("#run"); await Status(page,"Running");
    await page.WaitForFunctionAsync("document.getElementById('machineRegisters').textContent.includes('БРЗ') && document.getElementById('machineRegisters').textContent.includes('БРС')");
    await page.ClickAsync("#stop"); await Status(page,"Halted"); await page.ClickAsync("#reset"); await Status(page,"Idle");
    await page.ClickAsync("#editorTab"); await page.ClickAsync("#machineTemplate");
    await page.WaitForFunctionAsync("document.getElementById('sourceText').value.includes('memoryWords')");
    await page.SelectOptionAsync("#machineModel","supervisor-functional"); await page.CheckAsync("input[value=debug]");
    await page.ClickAsync("#sourceRun"); await Status(page,"Paused"); await page.ClickAsync("#step");
    await page.WaitForFunctionAsync("document.getElementById('instructionCount').textContent==='1 instruction'");
    await page.ClickAsync("#resume"); await Status(page,"Completed");
    if(!(await page.Locator("#machineStateInfo").TextContentAsync())!.Contains("Functional")) throw new Exception("Functional owner was not selected");
    await page.ClickAsync("#sourceLoad");
    await page.SelectOptionAsync("#machineModel","supervisor-hardware"); await page.CheckAsync("input[value=full]");
    await page.ClickAsync("#run"); await Status(page,"Completed");
    await page.WaitForFunctionAsync("document.getElementById('machineStateInfo').textContent.includes('Hardware') && document.getElementById('machineRegisters').textContent.includes('БРЧ[3]') && document.getElementById('machineRegisters').textContent.includes('M20 ') && document.getElementById('machineRegisters').textContent.includes('M37 ')");
    await page.Locator("#extendedState").ScrollIntoViewIfNeededAsync();
    await page.ScreenshotAsync(new(){Path=Path.Combine(output,"machine-registers.png"),FullPage=true});
});
await Check("machine-format-selection", async (page, _) =>
{
    await page.SelectOptionAsync("#machineModel", "supervisor-hardware");
    await Upload(page);
    if (await page.Locator("#machineModel").InputValueAsync() != "dubna") throw new Exception("DUB upload retained supervisor");
    await page.SelectOptionAsync("#machineModel", "supervisor-functional");
    await page.WaitForFunctionAsync("document.getElementById('fileName').textContent==='machines/supervisor-functional.json' && !document.getElementById('run').disabled");
    await page.ClickAsync("#run");
    await Status(page,"Completed");
    await page.ClickAsync("#editorTab");
    await page.FillAsync("#sourceText", tiny);
    await page.CheckAsync("input[value=full]"); await page.ClickAsync("#sourceRun"); await Status(page,"Completed");
    if (await page.Locator("#machineModel").InputValueAsync() != "dubna") throw new Exception("DUB text retained supervisor");
    await page.ClickAsync("#machineTemplate");
    await page.WaitForFunctionAsync("document.getElementById('sourceText').value.includes('memoryWords')");
    await page.SelectOptionAsync("#machineModel", "dubna");
    await page.ClickAsync("#sourceRun"); await Status(page,"Completed");
    if (await page.Locator("#machineModel").InputValueAsync() != "supervisor-functional") throw new Exception("JSON text retained Dubna");
    if (!string.IsNullOrEmpty(await page.Locator("#error").TextContentAsync())) throw new Exception("Compatible job failed");
});
await Check("supervisor-default-template", async (page, _) =>
{
    await page.SelectOptionAsync("#machineModel","supervisor-hardware");
    await page.WaitForFunctionAsync("document.getElementById('fileName').textContent==='machines/supervisor-hardware.json' && !document.getElementById('run').disabled");
    if(!await page.Locator("input[value=full]").IsCheckedAsync()) throw new Exception("Default supervisor should be ready for full execution");
    await page.ClickAsync("#run"); await Status(page,"Completed");
    await page.WaitForFunctionAsync("document.getElementById('machineRegisters').textContent.includes('БРЧ[3]')");
    if(!string.IsNullOrEmpty(await page.Locator("#error").TextContentAsync())) throw new Exception("Default supervisor failed");
});
await Check("memory-decode", async (page, _) =>
{
    await Upload(page); await page.ClickAsync("#run"); await Status(page,"Paused");
    await page.FillAsync("#address","1000"); await page.ClickAsync("#inspect");
    await page.WaitForFunctionAsync("document.getElementById('memory').textContent.startsWith('01000  ') && document.getElementById('memory').textContent.includes('atx 1234')");
    string[] before=(await page.Locator("#memory").TextContentAsync())!.Split('\n').Select(line => string.Join(" ",line.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2))).ToArray();
    await page.UncheckAsync("#decodeMemory");
    await page.WaitForFunctionAsync("window.__events.findLast(e=>e.type==='memory').data.words[0].disassembly===null");
    string[] after=(await page.Locator("#memory").TextContentAsync())!.Split('\n').Select(line => string.Join(" ",line.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(2))).ToArray();
    if(!before.SequenceEqual(after) || (await page.Locator("#memory").TextContentAsync())!.Contains("atx")) throw new Exception("Decode toggle changed raw memory");
});
await Check("layout", async (page, _) =>
{
    await Upload(page); await page.ClickAsync("#run"); await Status(page, "Paused");
    foreach (var (width, height) in new[] { (1440,900), (1280,720), (1024,768), (768,1024), (390,844), (320,740) })
    {
        await page.SetViewportSizeAsync(width, height);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"layout-{width}.png"), FullPage = true });
        var metrics = await page.EvaluateAsync<JsonElement>("""
        (()=>({width:innerWidth,scrollWidth:document.documentElement.scrollWidth,height:document.documentElement.scrollHeight,
          overflow:[...document.querySelectorAll('body *')].filter(e=>{const r=e.getBoundingClientRect();return r.width>0&&(r.right>innerWidth+1||r.left< -1)}).map(e=>e.id||e.tagName).slice(0,20),
          unlabeled:[...document.querySelectorAll('input,select')].filter(e=>e.type!=='radio'&&!e.labels?.length&&!e.getAttribute('aria-label')).map(e=>e.id),
          panels:[...document.querySelectorAll('main>.row>section')].map(e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height}})}))()
        """);
        Console.WriteLine("LAYOUT " + metrics);
        layouts.Add(metrics);
        if (metrics.GetProperty("scrollWidth").GetInt32() > width || metrics.GetProperty("overflow").GetArrayLength() > 0)
            throw new Exception("Horizontal overflow at " + width);
        if (metrics.GetProperty("unlabeled").GetArrayLength() > 0) throw new Exception("Unlabeled form controls");
        var panels = metrics.GetProperty("panels");
        if (panels.GetArrayLength() != 2) throw new Exception("Expected exactly two workspace columns");
        if (width >= 768 && panels[0].GetProperty("y").GetDouble() != panels[1].GetProperty("y").GetDouble())
            throw new Exception("Workspace columns are not side by side");
        if (width >= 768 && !await page.EvaluateAsync<bool>("(()=>{const j=document.querySelector('.job-panel').getBoundingClientRect();const r=document.getElementById('registers').getBoundingClientRect();const p=document.querySelector('.control-panel').getBoundingClientRect();return j.height<330&&r.bottom<=p.bottom})()"))
            throw new Exception("Job hides registers at " + width);
        if (!await page.EvaluateAsync<bool>("document.querySelector('.control-panel').contains(document.getElementById('jobHeading')) && document.querySelector('.control-panel').contains(document.getElementById('registers')) && document.querySelector('.control-panel').contains(document.getElementById('memory'))"))
            throw new Exception("Job and machine state must be in the left column");
        if (await page.Locator(".job-options").EvaluateAsync<int>("e=>new Set([...e.children].map(c=>c.getBoundingClientRect().left)).size") != 1)
            throw new Exception("Job fields must use a single column");
    }
    foreach (int width in new[] { 1440, 768, 320 })
    {
        await page.SetViewportSizeAsync(width, 900);
        foreach (string tab in new[] { "editorTab", "storageTab", "terminalTab", "punchTab", "cardTab", "instructionTab" })
        {
            await page.ClickAsync("#" + tab);
            await page.WaitForFunctionAsync("tab=>document.querySelector(document.getElementById(tab).dataset.bsTarget).classList.contains('show')", tab);
            if (await page.EvaluateAsync<int>("document.documentElement.scrollWidth") > width) throw new Exception("Horizontal overflow on " + tab + " at " + width);
        }
    }
    await page.ClickAsync("#printTab");
    await page.EvaluateAsync("document.getElementById('error').textContent='x'.repeat(2000); document.getElementById('fileName').textContent='long'.repeat(80)+'.dub'; document.getElementById('printOutput').textContent='0123456789'.repeat(20)+'\\n'+'Second line';");
    if (await page.EvaluateAsync<int>("document.documentElement.scrollWidth") > 320) throw new Exception("Long content breaks mobile layout");
    if (await page.Locator("#printOutput").EvaluateAsync<string>("e=>getComputedStyle(e).whiteSpace") != "pre") throw new Exception("Fixed width output wraps");
    await page.ScreenshotAsync(new() { Path = Path.Combine(output, "long-content-mobile.png"), FullPage = true });
});
await Check("accessibility", async (page, _) =>
{
    await Upload(page); await page.ClickAsync("#run"); await Status(page, "Paused");
    string axePath = Path.Combine(output, "axe.min.js");
    if (!File.Exists(axePath))
    {
        using var http = new HttpClient();
        await File.WriteAllTextAsync(axePath, await http.GetStringAsync("https://cdn.jsdelivr.net/npm/axe-core@4.10.3/axe.min.js"));
    }
    await page.AddScriptTagAsync(new() { Path = axePath });
    var audits = new List<object>();
    foreach (string tab in new[] { "printTab", "cardTab", "examplesTab", "editorTab", "storageTab", "terminalTab", "punchTab", "instructionTab" })
    {
        await page.ClickAsync("#" + tab);
        var violations = await page.EvaluateAsync<JsonElement>("async()=>{const result=await axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21a','wcag21aa']}});return result.violations.map(v=>({id:v.id,impact:v.impact,description:v.description,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))}));}");
        audits.Add(new { tab, violations });
        await File.WriteAllTextAsync(Path.Combine(output, "accessibility.json"), JsonSerializer.Serialize(audits, new JsonSerializerOptions { WriteIndented = true }));
        if (violations.GetArrayLength() > 0) throw new Exception("Accessibility violations on " + tab + ": " + violations);
    }
});
await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { results, errors, layouts }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Browser errors: " + JsonSerializer.Serialize(errors));
Environment.ExitCode = failures > 0 || errors.Count > 0 ? 1 : 0;
