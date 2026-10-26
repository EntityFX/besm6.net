(() => {
  const t = Besm6I18n.t, ui = (id, value) => Besm6I18n.text(document.getElementById(id), value);
  const $ = id => document.getElementById(id);
  let entries = [], pending;
  const changed = () => window.dispatchEvent(new Event('exampleSelectionChanged'));
  function options() {
    const selected = $('exampleSelect').value, filter = $('exampleFilter').value.toLowerCase();
    const placeholder = document.createElement('option'); placeholder.value = ''; placeholder.textContent = t('Select an example');
    const groups = new Map();
    for (const entry of entries.filter(entry => entry.id.toLowerCase().includes(filter))) {
      if (!groups.has(entry.group)) { const group = document.createElement('optgroup'); group.label = entry.group; groups.set(entry.group, group); }
      const option = document.createElement('option'); option.value = entry.id; option.textContent = entry.name; groups.get(entry.group).append(option);
    }
    $('exampleSelect').replaceChildren(placeholder, ...groups.values());
    $('exampleSelect').value = selected;
    if (!$('exampleSelect').value) {
      $('exampleSelect').value = '';
      pending?.abort(); ui('exampleSource', 'Выберите пример для просмотра.');
    }
    ui('exampleMessage', `${entries.filter(entry => entry.id.toLowerCase().includes(filter)).length} примеров`);
    changed();
  }
  window.addEventListener('languagechange', options);
  $('exampleFilter').addEventListener('input', options);
  $('exampleSelect').addEventListener('change', async () => {
    pending?.abort(); pending = new AbortController();
    changed();
    const id = $('exampleSelect').value;
    if (!id) { ui('exampleSource', 'Выберите пример для просмотра.'); return; }
    ui('exampleSource', 'Загрузка исходного текста…');
    try {
      const response = await fetch('/api/examples/source?id=' + encodeURIComponent(id), { signal: pending.signal });
      if (!response.ok) throw new Error('Не удалось прочитать пример.');
      const text = await response.text();
      if ($('exampleSelect').value === id) { Besm6Source.render($('exampleSource'), text); $('exampleSource').dataset.loaded = id; ui('exampleMessage', id); changed(); }
    } catch (error) { if (error.name !== 'AbortError') ui('exampleSource', error.message); }
  });
  fetch('/api/examples').then(response => { if (!response.ok) throw new Error('Не удалось загрузить список примеров.'); return response.json(); }).then(data => {
    entries = data; options(); $('exampleSelect').disabled = !entries.length;
  }).catch(error => { ui('exampleMessage', error.message); });
})();
