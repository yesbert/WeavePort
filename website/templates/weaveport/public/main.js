class LanguageExamples {
    constructor(example) {
        this.example = example;
        this.tablist = example.querySelector('[role="tablist"]');
        this.tabs = [...example.querySelectorAll('[data-language]')];
        this.panels = [...example.querySelectorAll('[data-language-panel]')];
    }

    initialize() {
        if (!this.tablist || this.tabs.length === 0) return;
        this.tabs.forEach((tab, index) => this.bindTab(tab, index));
        this.select(this.tabs[0]);
        this.tablist.hidden = false;
        this.example.classList.add('wp-enhanced');
    }

    bindTab(tab, index) {
        tab.addEventListener('click', () => this.select(tab));
        tab.addEventListener('keydown', (event) => this.navigate(event, index));
    }

    select(tab, focus = false) {
        for (const item of this.tabs) {
            const selected = item === tab;
            item.setAttribute('aria-selected', String(selected));
            item.tabIndex = selected ? 0 : -1;
        }
        for (const panel of this.panels) {
            panel.hidden = panel.dataset.languagePanel !== tab.dataset.language;
            panel.setAttribute('role', 'tabpanel');
            panel.setAttribute('aria-labelledby', `tab-${panel.dataset.languagePanel}`);
            panel.tabIndex = 0;
        }
        if (focus) tab.focus();
    }

    navigate(event, index) {
        const next = this.nextIndex(event.key, index);
        if (next === undefined) return;
        event.preventDefault();
        this.select(this.tabs[next], true);
    }

    nextIndex(key, index) {
        switch (key) {
            case 'ArrowRight':
                return (index + 1) % this.tabs.length;
            case 'ArrowLeft':
                return (index + this.tabs.length - 1) % this.tabs.length;
            case 'Home':
                return 0;
            case 'End':
                return this.tabs.length - 1;
            default:
                return undefined;
        }
    }
}

function addSkipLink() {
    const main = document.querySelector('main');
    if (!main) return;
    main.id = 'main-content';
    const skip = document.createElement('a');
    skip.href = '#main-content';
    skip.textContent = 'Skip to content';
    skip.className = 'visually-hidden-focusable position-absolute p-3 bg-body';
    skip.style.zIndex = '2000';
    document.body.prepend(skip);
}

export default {
    defaultTheme: 'light',
    iconLinks: [{ icon: 'github', href: 'https://github.com/yesbert/WeavePort', title: 'GitHub' }],
    start: () => {
        addSkipLink();
        for (const example of document.querySelectorAll('[data-language-example]')) {
            new LanguageExamples(example).initialize();
        }
    },
};
