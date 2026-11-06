// Run in the page by the firefox tool (WebDriver BiDi script.callFunction). Returns an array: a JSON
// string describing the page, then the DOM elements it lists, in the same order - the browser hands each
// back as a node reference the tool clicks or types into.
//
// Element keys: k kind (0 heading, 1 region, 2 link, 3 control), r role, n accessible name, l heading
// level, c index of the enclosing region (-1 none), t text (a heading: its section; a control: its
// value, never a password), h link target, a id attribute, v 0 when not rendered visibly.
(maxElements, maxSectionText, maxPageText) => {
  const SKIPPED = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE', 'HEAD']);
  const REGION_TAGS = {
    NAV: 'navigation', MAIN: 'main', ASIDE: 'complementary', HEADER: 'banner', FOOTER: 'contentinfo',
    FORM: 'form', DIALOG: 'dialog', SEARCH: 'search',
  };
  const REGION_ROLES = new Set([
    'navigation', 'main', 'complementary', 'banner', 'contentinfo', 'form', 'search', 'dialog',
    'alertdialog', 'region',
  ]);
  const CONTROL_ROLES = new Set([
    'button', 'checkbox', 'radio', 'switch', 'tab', 'menuitem', 'menuitemcheckbox', 'menuitemradio',
    'combobox', 'listbox', 'textbox', 'searchbox', 'slider', 'spinbutton', 'option',
  ]);
  const INPUT_ROLES = {
    checkbox: 'checkbox', radio: 'radio', range: 'slider', number: 'spinbutton', search: 'searchbox',
    submit: 'button', button: 'button', reset: 'button', image: 'button', file: 'file',
  };
  const NAME_LENGTH = 160;

  const clean = (text, max) => {
    const collapsed = (text || '').replace(/\s+/g, ' ').trim();
    return collapsed.length > max ? collapsed.slice(0, max) : collapsed;
  };

  const labelled = (el) => {
    const label = el.getAttribute('aria-label');
    if (label && label.trim()) return label;
    const ids = el.getAttribute('aria-labelledby');
    if (ids) {
      const text = ids.split(/\s+/).map((id) => document.getElementById(id)?.textContent || '').join(' ');
      if (text.trim()) return text;
    }
    return '';
  };

  const nameOf = (el, kind) => {
    const given = labelled(el);
    if (given) return given;
    if (kind === 1) return el.title || el.getAttribute('name') || '';
    if (el.labels && el.labels.length) {
      const text = [...el.labels].map((label) => label.textContent).join(' ');
      if (text.trim()) return text;
    }
    const tag = el.tagName;
    if (tag === 'INPUT') {
      const type = (el.type || '').toLowerCase();
      if ((type === 'submit' || type === 'button' || type === 'reset') && el.value) return el.value;
      if (type === 'image' && el.alt) return el.alt;
      return el.placeholder || el.title || el.name || '';
    }
    if (tag === 'SELECT' || tag === 'TEXTAREA') return el.placeholder || el.title || el.name || '';
    if (el.textContent && el.textContent.trim()) return el.textContent;
    const image = el.querySelector('img[alt]');
    return (image && image.alt) || el.title || '';
  };

  const valueOf = (el) => {
    const tag = el.tagName;
    if (tag === 'INPUT') {
      const type = (el.type || 'text').toLowerCase();
      if (type === 'password') return '';
      if (type === 'checkbox' || type === 'radio') return el.checked ? 'checked' : 'unchecked';
      if (type in INPUT_ROLES && INPUT_ROLES[type] !== 'slider' && INPUT_ROLES[type] !== 'spinbutton'
        && INPUT_ROLES[type] !== 'searchbox') return '';
      return el.value || '';
    }
    if (tag === 'TEXTAREA') return el.value || '';
    if (tag === 'SELECT') return [...el.selectedOptions].map((option) => option.textContent).join(', ');
    const checked = el.getAttribute('aria-checked');
    if (checked) return checked === 'true' ? 'checked' : 'unchecked';
    return '';
  };

  // [kind, role, heading level] of an element the outline lists; null for the others.
  const classify = (el) => {
    const tag = el.tagName;
    const role = (el.getAttribute('role') || '').trim().toLowerCase();
    if (/^H[1-6]$/.test(tag)) return [0, 'heading', Number(tag[1])];
    if (role === 'heading') return [0, 'heading', Number(el.getAttribute('aria-level')) || 2];
    if (REGION_ROLES.has(role)) return [1, role, 0];
    if (tag in REGION_TAGS) return [1, REGION_TAGS[tag], 0];
    if (role === 'link' || (tag === 'A' && el.hasAttribute('href'))) return [2, 'link', 0];
    if (CONTROL_ROLES.has(role)) return [3, role, 0];
    if (tag === 'BUTTON' || tag === 'SUMMARY') return [3, 'button', 0];
    if (tag === 'TEXTAREA') return [3, 'textbox', 0];
    if (tag === 'SELECT') return [3, el.multiple ? 'listbox' : 'combobox', 0];
    if (tag === 'INPUT') {
      const type = (el.type || 'text').toLowerCase();
      return type === 'hidden' ? null : [3, INPUT_ROLES[type] || 'textbox', 0];
    }
    if (el.isContentEditable && !(el.parentElement && el.parentElement.isContentEditable)) return [3, 'textbox', 0];
    return null;
  };

  const rendered = (el) => {
    if (el.hidden) return false;
    if (typeof el.checkVisibility !== 'function' || el.checkVisibility()) return true;
    return getComputedStyle(el).display === 'contents';
  };

  const visible = (el, kind) => {
    if (typeof el.checkVisibility === 'function'
      && !el.checkVisibility({ visibilityProperty: true, opacityProperty: true })) return false;
    if (kind < 2) return true;
    const box = el.getBoundingClientRect();
    return box.width > 0 && box.height > 0;
  };

  const elements = [];
  const nodes = [];
  const regions = new Map();
  let lead = '';
  let heading = -1;
  let truncated = false;

  const walker = document.createTreeWalker(
    document.body || document.documentElement,
    NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT,
    {
      acceptNode: (node) => (node.nodeType === 1 && (SKIPPED.has(node.tagName) || !rendered(node))
        ? NodeFilter.FILTER_REJECT
        : NodeFilter.FILTER_ACCEPT),
    });

  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (node.nodeType === 3) {
      const text = node.nodeValue;
      if (!text || !text.trim()) continue;
      if (heading < 0) {
        if (lead.length < maxSectionText) lead += ' ' + text;
      } else if (!nodes[heading].contains(node) && elements[heading].t.length < maxSectionText) {
        elements[heading].t += ' ' + text;
      }
      continue;
    }

    const kinds = classify(node);
    if (!kinds) continue;
    if (elements.length >= maxElements) {
      truncated = true;
      break;
    }

    const [kind, role, level] = kinds;
    let container = -1;
    for (let parent = node.parentElement; parent; parent = parent.parentElement) {
      if (regions.has(parent)) {
        container = regions.get(parent);
        break;
      }
    }

    const element = { k: kind, r: role, n: clean(nameOf(node, kind), NAME_LENGTH), c: container, t: '' };
    if (kind === 0) element.l = level;
    if (kind === 3) element.t = clean(valueOf(node), NAME_LENGTH);
    if (kind === 2) {
      const raw = node.getAttribute('href') || '';
      element.h = raw.startsWith('#') ? raw : (node.href || raw);
    }
    if (node.id) element.a = node.id;
    if (!visible(node, kind)) element.v = 0;

    if (kind === 1) regions.set(node, elements.length);
    if (kind === 0) heading = elements.length;
    elements.push(element);
    nodes.push(node);
  }

  for (const element of elements) {
    if (element.k === 0) element.t = clean(element.t, maxSectionText);
  }

  const pageText = (document.body && document.body.innerText) || '';
  const outline = {
    url: location.href,
    title: document.title || '',
    lead: clean(lead, maxSectionText),
    text: pageText.length > maxPageText ? pageText.slice(0, maxPageText) : pageText,
    textTruncated: pageText.length > maxPageText,
    truncated,
    elements,
  };
  return [JSON.stringify(outline), ...nodes];
}
