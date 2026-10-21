(() => {
  const $ = id => document.getElementById(id);
  let entries = [], pending;
  const changed = () => window.dispatchEvent(new Event('exampleSelectionChanged'));
  function options() {
    const selected = $('exampleSelect').value, filter = $('exampleFilter').value.toLowerCase();
    const placeholder = document.createElement('option'); placeholder.value = ''; placeholder.textContent = 'Выберите пример';
    const groups = new Map();
    for (const entry of entries.filter(entry => entry.id.toLowerCase().includes(filter))) {
      if (!groups.has(entry.group)) { const group = document.createElement('optgroup'); group.label = entry.group; groups.set(entry.group, group); }
      const option = document.createElement('option'); option.value = entry.id; option.textContent = entry.name; groups.get(entry.group).append(option);
    }
    $('exampleSelect').replaceChildren(placeholder, ...groups.values());
    $('exampleSelect').value = selected;
    if (!$('exampleSelect').value) {
      $('exampleSelect').value = '';
      pending?.abort(); $('exampleSource').textContent = 'Выберите пример для просмотра.';
    }
    $('exampleMessage').textContent = `${entries.filter(entry => entry.id.toLowerCase().includes(filter)).length} примеров`;
    changed();
  }
  $('exampleFilter').addEventListener('input', options);
  $('exampleSelect').addEventListener('change', async () => {
    pending?.abort(); pending = new AbortController();
    changed();
    const id = $('exampleSelect').value;
    if (!id) { $('exampleSource').textContent = 'Выберите пример для просмотра.'; return; }
    $('exampleSource').textContent = 'Загрузка исходного текста…';
    try {
      const response = await fetch('/api/examples/source?id=' + encodeURIComponent(id), { signal: pending.signal });
      if (!response.ok) throw new Error('Не удалось прочитать пример.');
      const text = await response.text();
      if ($('exampleSelect').value === id) { $('exampleSource').textContent = text; $('exampleMessage').textContent = id; }
    } catch (error) { if (error.name !== 'AbortError') $('exampleSource').textContent = error.message; }
  });
  fetch('/api/examples').then(response => { if (!response.ok) throw new Error('Не удалось загрузить список примеров.'); return response.json(); }).then(data => {
    entries = data; options(); $('exampleSelect').disabled = !entries.length;
  }).catch(error => { $('exampleMessage').textContent = error.message; });
})();
