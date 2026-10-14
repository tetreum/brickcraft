class Seger {

    constructor () {
        this.mainContainer = document.getElementById('main-container');

        this.getConfig().then(() => {
            this.language = this.pickLanguage();
            this.setup();

            this.goTo(this.getCurrentRoute());
        });
    }

    setup () {
        if (typeof this.config.logo === "undefined") {
            document.querySelector('.navbar-brand').innerText = this.config.title;
        } else {
            let img = document.createElement("img");
            img.src = this.config.logo;
            img.title = this.config.title;
            document.querySelector('.navbar-brand').prepend(img);
        }

        this.setupLanguageSelector();
        this.translatePage();
        this.setupListeners();
    }

    goTo (route) {
        const splitted = route.split("/");

        if (route.length === 0) {
            return this.showHome();
        } else if (splitted[0] === 'post') {
            const slug = splitted[1];
            return this.showPost(slug);
        } else {
            this.showPage(route);
        }
    }

    // -------- languages --------
    // config.json lists them ("languages": { "en": "English", ... }, "defaultLanguage"); an entry's title and
    // preview are one per language ({ "en": "...", "es": "..." }) or plain text, and its post is
    // posts/[slug]/index.[language].html

    getLanguages () {
        return Object.keys(this.config.languages || {});
    }

    getDefaultLanguage () {
        return this.config.defaultLanguage || this.getLanguages()[0] || 'en';
    }

    // the one in the url (?lang=es), else the one picked before, else the browser's, else the blog's default
    pickLanguage () {
        const languages = this.getLanguages();
        const fromUrl = new URLSearchParams(location.search).get('lang');

        if (languages.includes(fromUrl)) {
            this.saveLanguage(fromUrl);
            return fromUrl;
        }
        let saved = null;
        try {
            saved = localStorage.getItem("language");
        } catch (e) {
        }
        if (languages.includes(saved)) {
            return saved;
        }
        const browser = this.getBrowserLanguage().toLowerCase().split('-')[0];

        return languages.includes(browser) ? browser : this.getDefaultLanguage();
    }

    saveLanguage (language) {
        try {
            localStorage.setItem("language", language);
        } catch (e) {
        }
    }

    getBrowserLanguage () {
        return navigator.userLanguage || (navigator.languages && navigator.languages.length && navigator.languages[0]) || navigator.language || navigator.browserLanguage || navigator.systemLanguage || 'en';
    }

    getLanguage () {
        return this.language || this.getDefaultLanguage();
    }

    setLanguage (language) {
        if (!this.getLanguages().includes(language) || language === this.language) {
            return;
        }
        this.language = language;
        this.saveLanguage(language);

        // the url keeps it, so a shared link opens in it (built by hand: URLSearchParams would encode the route)
        const params = location.search.replace(/^\?/, '').split('&').filter(part => part.length > 0 && !part.startsWith('lang='));
        params.push('lang=' + language);
        history.replaceState({}, '', '?' + params.join('&') + location.hash);

        this.translatePage();
        this.goTo(this.getCurrentRoute());
    }

    // a text in the current language: plain text, or one per language (falling back to the default, then any)
    text (value, language) {
        if (value === null || typeof value !== "object") {
            return value;
        }
        language = language || this.getLanguage();

        if (typeof value[language] !== "undefined") {
            return value[language];
        }
        if (typeof value[this.getDefaultLanguage()] !== "undefined") {
            return value[this.getDefaultLanguage()];
        }
        return Object.values(value)[0];
    }

    // one of the blog's own labels (config.json -> strings), the key itself if there's none
    t (key) {
        const strings = this.config.strings || {};
        const language = strings[this.getLanguage()] || {};
        const fallback = strings[this.getDefaultLanguage()] || {};

        return language[key] || fallback[key] || key;
    }

    // the languages an entry is written in: its title's, or every one for a plain text title
    getEntryLanguages (entry) {
        if (entry.title !== null && typeof entry.title === "object") {
            return Object.keys(entry.title);
        }
        return this.getLanguages();
    }

    // the language an entry is read in: the current one if it has it
    getEntryLanguage (entry) {
        const languages = this.getEntryLanguages(entry);

        if (languages.includes(this.getLanguage())) {
            return this.getLanguage();
        }
        return languages.includes(this.getDefaultLanguage()) ? this.getDefaultLanguage() : languages[0];
    }

    setupLanguageSelector () {
        const container = document.getElementById('language-selector');
        const languages = this.getLanguages();

        if (container === null || languages.length < 2) {
            return;
        }
        languages.forEach(language => {
            let button = document.createElement("button");
            button.type = "button";
            button.className = "btn btn-sm btn-link nav-link";
            button.dataset.language = language;
            button.innerText = language.toUpperCase();
            button.title = this.config.languages[language];
            button.addEventListener("click", () => this.setLanguage(language));
            container.appendChild(button);
        });
    }

    // the page's labels outside the templates ([data-i18n="key"]), the language selector, <html lang>
    translatePage () {
        document.documentElement.lang = this.getLanguage();
        document.querySelectorAll('[data-i18n]').forEach(el => {
            el.innerText = this.t(el.dataset.i18n);
        });
        document.querySelectorAll('#language-selector [data-language]').forEach(button => {
            const active = button.dataset.language === this.getLanguage();
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        });
    }

    formatDate(date) {
        return (new Date(date)).toLocaleString(this.getLanguage(), {
            weekday: 'long',
            year: 'numeric',
            month: 'long',
            day: 'numeric'});
    }

    getCurrentRoute () {
        return this.getRoute(location.search);
    }

    getRoute (queryString) {
        if (queryString.length === 0) {
            return "";
        }
        const route = (new URLSearchParams(queryString)).entries().next().value;

        if (!route[0].startsWith("/") || route[1].length > 0) {
            return "";
        }

        return route[0].substr(1);
    }

    setupListeners () {
        document.addEventListener("click", (e) => {
            const a = e.target.closest("a");

            // not clicking a local link
            if (a == null ||
                !a.href.startsWith(location.origin + location.pathname) ||
                a.target.length > 0
            ) {
                return;
            }
            // avoid redirecting but change the url
            e.preventDefault();
            history.pushState({}, '', a.href);

            // same url but with an anchor
            if (a.href.startsWith(location.href.replace(location.hash, "")) && a.href.includes('#')) {
                document.getElementById(a.href.split('#')[1]).scrollIntoView();
                return;
            }

            const route = this.getRoute(new URL(a.href).search);

            this.goTo(route);
        });

        // detect when user goes back/forward through navigation history
        window.addEventListener("popstate", (e) => {
            this.goTo(this.getCurrentRoute());
        });
    }

    getTemplate(name) {
        return document.getElementById("tpl-" + name).innerHTML.trim();
    }

    parseTemplate(name, data) {
        let parsedTemplate = _.template(this.getTemplate(name))(data);
        return this.stringToDOM(parsedTemplate);
    }

    stringToDOM (str) {
        let div = document.createElement('div');
        div.innerHTML = str;

        return div.firstChild;
    }

    getEntry (slug) {
        const entry = this.config.entries.find(entry => entry.slug === slug);

        return entry ? Promise.resolve(entry) : Promise.reject();
    }

    // a post's html in that language, null if it isn't written in it
    getPostHtml (slug, language) {
        return fetch('posts/' + slug + "/index." + language + ".html")
            .then(response => response.ok ? response.text() : null)
            .then(html => {
                if (html === null) {
                    return null;
                }
                // the post's own files are next to it
                return html.replace(/src="(?!https?:|\/\/|data:|\/)([^"]+)"/g, 'src="posts/' + slug + '/$1"');
            });
    }

    // the entry with its html (entry.html) in the language it's read in (entry.language)
    getFullEntry (slug) {
        return this.getEntry(slug).then(entry => {
            const language = this.getEntryLanguage(entry);
            entry.htmls = entry.htmls || {};

            if (typeof entry.htmls[language] !== 'undefined') {
                return Object.assign({}, entry, {html: entry.htmls[language], language});
            }
            return this.getPostHtml(slug, language).then(html => {
                if (html === null) {
                    return Promise.reject();
                }
                entry.htmls[language] = html;

                return Object.assign({}, entry, {html, language});
            });
        }).catch(() => {
            const draft = this.getDraft();

            if (draft === null || draft.slug !== slug) {
                return Promise.reject();
            }
            return draft;
        });
    }

    getDraft () {
        let draft = null;

        try {
            draft = localStorage.getItem("draft");
        } catch (e) {
        }
        if (draft === null) {
            return null;
        }

        return JSON.parse(draft);
    }

    getConfig () {
        return fetch('config.json')
          .then(response => response.json())
          .then(config => {
              this.config = config;

              this.config.entries.forEach(entry => {
                  entry.url = "?/post/" + entry.slug;
              });
          });
    }

    setTitle (title) {
        document.title = title + " - " + this.config.title;
    }

    getPreviousAndNextPosts (slug) {
        let entry;
        let related = {
            previous: null,
            next: null,
        };
        const count = this.config.entries.length;

        for (let i = 0; i < this.config.entries.length; i++) {
            entry = this.config.entries[i];

            if (entry.slug === slug) {
                if (i > 0) {
                    related.previous = this.config.entries[i - 1];
                }

                i++;

                if (i < count) {
                    related.next = this.config.entries[i];
                }
                break;
            }
        }

        return related;
    }

    showPost (slug) {
        this.getFullEntry(slug).then((entry) => {
            const relatedPosts = this.getPreviousAndNextPosts(slug);
            const title = this.text(entry.title, entry.language);
            const preview = this.text(entry.preview, entry.language);
            // read in another language: it isn't written in this one yet
            const untranslated = typeof entry.language !== "undefined" && entry.language !== this.getLanguage();

            this.render(this.parseTemplate("post", {entry, title, relatedPosts, untranslated}));
            this.setTitle(title);
            this.setMetas([
                {
                    property: "og:title",
                    content: title,
                },
                {
                    name: "twitter:title",
                    content: title,
                },
                {
                    name: "description",
                    content: preview,
                },
                {
                    name: "twitter:description",
                    content: preview,
                },
                {
                    property: "og:description",
                    content: preview,
                }
            ]);
        }).catch(() => {
            this.show404();
        });
    }

    showHome () {
        this.setTitle(this.t("home"));
        this.render(this.parseTemplate("home", {
            entries: this.config.entries
        }));
    }

    slugToString (slug) {
        slug = slug.charAt(0).toUpperCase() + slug.slice(1);
        slug = slug.replace("-", " ");

        return slug;
    }

    showPage (route) {
        return fetch(route + '.html')
          .then(response => {
            if (response.status === 404) {
                return null;
            }
            return response.text();
          }).then(html => {
              if (html === null) {
                  return this.show404();
              }
              this.setTitle(this.slugToString(route));

              html = this.stringToDOM(html);
              this.render(html);

              // appendChild won't load/execute scripts
              // so we have to do it manually
              html.querySelectorAll("script").forEach(el => {
                  let newScript = document.createElement("script");

                  if (el.src.length > 0) {
                    newScript.src = el.src;
                  } else {
                    var inlineScript = document.createTextNode(el.innerText);
                    newScript.appendChild(inlineScript);
                  }
                  document.body.appendChild(newScript);
              });
          });
    }

    show404 () {
        this.setTitle(this.t("notFoundTitle"));
        this.render(this.parseTemplate("404"));
    }

    render (html) {
        this.setMetas([]);

        if (typeof html === "string") {
            html = this.stringToDOM(html);
        }
        this.mainContainer.innerHTML = '';
        this.mainContainer.appendChild(html);

        window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
    }

    setMetas (list) {
        // remove previous entries (not the charset and viewport ones the page needs)
        document.querySelectorAll('meta[name]:not([name="viewport"]), meta[property]').forEach(el => {
            el.remove();
        });

        list.forEach(meta => {
            let tag = document.createElement("meta");

            if (typeof meta.name !== "undefined") {
                tag.name = meta.name;
            }
            if (typeof meta.property !== "undefined") {
                tag.setAttribute("property", meta.property);
            }
            if (typeof meta.content !== "undefined") {
                tag.content = meta.content;
            }
            document.head.appendChild(tag);
        });
    }
}
