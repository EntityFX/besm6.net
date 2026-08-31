using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Besm6.Core;
using Besm6.Loader;

namespace Besm6.Cli
{
    /// <summary>
    /// Команда `check` — batch-проверка всех .dub файлов в каталоге.
    /// </summary>
    public sealed class CheckCommand : ICommand
    {
        public string Name => "check";
        public string Description => "Batch-check all .dub files in a directory";
        public string Usage => "besm6 check <dir> [--limit N] [--config path]";

        public int Execute(string[] args)
        {
            string dir = args.Length >= 1 ? args[0] : "dubna/examples";
            long limit = 0;
            string? configPath = null;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--limit" when i + 1 < args.Length:
                        long.TryParse(args[++i], out limit);
                        break;
                    case "--config" when i + 1 < args.Length:
                        configPath = args[++i];
                        break;
                }
            }

            Config cfg = Config.Load(configPath);
            if (limit == 0) limit = cfg.CheckLimit;

            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Directory not found: {dir}");
                return 1;
            }

            var files = Directory.EnumerateFiles(dir, "*.dub", SearchOption.AllDirectories)
                .OrderBy(x => x).ToList();
            int passed = 0, parseFailed = 0, runFailed = 0, limitHit = 0;

            foreach (var f in files)
            {
                string rel = Path.GetRelativePath(dir, f);
                string status;
                try
                {
                    DubJob job = JobParser.ParseFile(f);
                    var loader = MachineFactory.CreateLoader(cfg);
                    loader.InstructionLimit = limit;
                    LoadResult result = loader.RunJob(job, File.ReadAllLines(f));
                    if (result.LimitExceeded)
                    {
                        status = $"LIMIT(pc=0{result.Pc:X})";
                        limitHit++;
                    }
                    else if (result.Success)
                    {
                        status = $"HALT({result.Instructions} instr)";
                        passed++;
                    }
                    else
                    {
                        status = $"ERR:{result.ErrorMessage}";
                        runFailed++;
                    }
                }
                catch (FormatException ex)
                {
                    status = $"PARSE-ERR: {ex.Message}";
                    parseFailed++;
                }
                catch (Exception ex)
                {
                    status = $"RUN-ERR: {ex.Message}";
                    runFailed++;
                }
                Console.WriteLine($"{status,-55}  {rel}");
            }

            Console.WriteLine();
            Console.WriteLine($"TOTAL: {files.Count}  OK: {passed}  LIMIT: {limitHit}  RUN-ERR: {runFailed}  PARSE-ERR: {parseFailed}");
            return (parseFailed == 0 && runFailed == 0 && limitHit == 0) ? 0 : 1;
        }
    }
}
