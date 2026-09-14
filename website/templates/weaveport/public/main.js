function initializeLanguageExamples() {
  document.querySelectorAll('[data-language-example]').forEach(example => {
    const tablist = example.querySelector('[role="tablist"]');
    const tabs = [...example.querySelectorAll('[data-language]')];
    const panels = [...example.querySelectorAll('[data-language-panel]')];
    const select = (tab, focus = false) => {
      tabs.forEach(item => {
        const selected = item === tab;
        item.setAttribute('aria-selected', String(selected));
        item.tabIndex = selected ? 0 : -1;
      });
      panels.forEach(panel => {
        panel.hidden = panel.dataset.languagePanel !== tab.dataset.language;
        panel.setAttribute('role', 'tabpanel');
        panel.setAttribute('aria-labelledby', `tab-${panel.dataset.languagePanel}`);
        panel.tabIndex = 0;
      });
      if (focus) tab.focus();
    };
    tabs.forEach((tab, index) => {
      tab.addEventListener('click', () => select(tab));
      tab.addEventListener('keydown', event => {
        let next;
        if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
        if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
        if (event.key === 'Home') next = 0;
        if (event.key === 'End') next = tabs.length - 1;
        if (next !== undefined) {
          event.preventDefault();
          select(tabs[next], true);
        }
      });
    });
    select(tabs[0]);
    tablist.hidden = false;
    example.classList.add('wp-enhanced');
  });
}

export default {
  defaultTheme: 'light',
  iconLinks: [{ icon: 'github', href: 'https://github.com/yesbert/WeavePort', title: 'GitHub' }],
  start: () => {
    const main = document.querySelector('main');
    if (main) {
      main.id = 'main-content';
      const skip = document.createElement('a');
      skip.href = '#main-content';
      skip.textContent = 'Skip to content';
      skip.className = 'visually-hidden-focusable position-absolute p-3 bg-body';
      skip.style.zIndex = '2000';
      document.body.prepend(skip);
    }
    initializeLanguageExamples();
  }
};
