class BlogEditor {
    constructor () {
        this.loadedInterval = setInterval(() => {
            if (typeof Jodit === "undefined" || typeof seger.config === "undefined") {
                return;
            }
            clearInterval(this.loadedInterval);

            this.onReady();
        }, 200);
    }

    onReady () {
        this.editor = new Jodit('#editor', {
	        buttons: [
	            'bold', 'strikethrough', 'underline', 'italic', 'eraser', '|',
	            'ul', 'ol', '|',
	            'outdent', 'indent', '|',
	            'fontsize', 'brush', 'paragraph', '|',
    	        'image', 'video', 'table', 'link', '|',
    	        'left', 'center', 'right', 'justify', '|',
    	        'undo', 'redo', '|', 'hr', 'source'
	        ]
        });
        this.dateInput = document.getElementById('date');
        this.titleInput = document.getElementById('title');
        this.languageSelector = document.getElementById('language');
        this.entrySelector = document.getElementById('existing-entry-selector');
        this.previewButton = document.getElementById('preview');

        this.dateInput.value = this.getTodayDate();

        this.setupLanguageSelector();
        this.setupEntrySelector();

        this.setupListeners();

		this.applyDraft();
    }

    setupListeners () {
        document.getElementById('blog-editor').addEventListener("submit", (e) => {
            e.preventDefault();
            this.generateEntry();
        });
        this.titleInput.addEventListener("change", () => {
            this.updatePreviewLink();
        });
        this.previewButton.addEventListener("click", () => {
            this.saveDraft();
        });

        // handle CRTL + S
		document.addEventListener("keydown", (e) => {
		  if (e.keyCode == 83 && (navigator.platform.match("Mac") ? e.metaKey : e.ctrlKey)) {
		    e.preventDefault();
		    this.saveDraft();
		  }
		}, false);
    }

    updatePreviewLink () {
        this.previewButton.href = "?/post/" + this.getSlug() + "&lang=" + this.languageSelector.value;
    }

    getTodayDate () {
        return new Date().toISOString().slice(0, 16);
    }

    saveDraft () {
        localStorage.setItem("draft", JSON.stringify(this.getCurrentEntry()));
    }

    // the blog's languages; changing it loads the entry's text in that language (empty if it isn't written in it)
    setupLanguageSelector () {
        const languages = seger.config.languages || {};

        Object.keys(languages).forEach(language => {
            let option = document.createElement("option");
            option.value = language;
            option.innerText = languages[language] + " (" + language + ")";
            this.languageSelector.appendChild(option);
        });
        this.languageSelector.value = seger.getLanguage();

        this.languageSelector.addEventListener("change", () => {
            if (this.entrySelector.value.length > 0) {
                this.recoverEntry();
            }
            this.updatePreviewLink();
        });
    }

    setupEntrySelector () {
        seger.config.entries.forEach(entry => {
            this.addEntryOption(entry);
        });

        this.entrySelector.addEventListener("change", (e) => {
            this.recoverEntry();
        });
    }

    addEntryOption (entry, prepend) {
        let option = document.createElement("option");

        option.innerText = seger.text(entry.title);
        option.value = entry.slug;

        if (prepend) {
            this.entrySelector.prepend(option);
        } else {
            this.entrySelector.appendChild(option);
        }
    }

    // the entry picked, in the language picked: an existing entry keeps its slug whatever the title
    recoverEntry () {
        const slug = this.entrySelector.value;
        const language = this.languageSelector.value;

        if (slug.length === 0) {
            this.editingSlug = null;
            this.applyEntry({
                html: '',
                title: '',
                date: this.getTodayDate(),
            });
            return;
        }
        seger.getEntry(slug).then(entry => {
            this.editingSlug = slug;
            const written = seger.getEntryLanguages(entry).includes(language);

            return (written ? seger.getPostHtml(slug, language) : Promise.resolve(null)).then(html => {
                this.applyEntry({
                    html: html || '',
                    title: written ? seger.text(entry.title, language) : '',
                    date: entry.date,
                });
            });
        }).catch(() => {
            // the draft of a new entry
            const draft = seger.getDraft();

            if (draft !== null && draft.slug === slug) {
                this.applyEntry(draft);
            }
        });
    }

    applyEntry (entry) {
        this.editor.value = entry.html;
        this.titleInput.value = entry.title;
        this.dateInput.value = entry.date.slice(0, 16);

        // trigger change event so preview url is updated
        this.titleInput.dispatchEvent(new Event('change'));
    }

    applyDraft () {
        const draft = seger.getDraft();

        if (draft === null) {
            return;
        }
        if (typeof draft.language !== "undefined") {
            this.languageSelector.value = draft.language;
        }
        // a translation of an existing entry keeps its slug
        this.editingSlug = seger.config.entries.some(entry => entry.slug === draft.slug) ? draft.slug : null;
        this.applyEntry(draft);

        draft.title = "Draft: " + draft.title;
        this.addEntryOption(draft, true);
    }

    // from https://gist.github.com/codeguy/6684588
    slugify (value) {
        return value
            .normalize('NFD') // split an accented letter in the base letter and the acent
            .replace(/[̀-ͯ]/g, '') // remove all previously split accents
            .toLowerCase()
            .trim()
            .replace(/[^a-z0-9 ]/g, '') // remove all chars not letters, numbers and spaces (to be replaced)
            .replace(/\s+/g, '-') // separator
    }

    getSlug () {
        return this.editingSlug || this.slugify(this.titleInput.value);
    }

    generatePreview (text) {
        text = text.replace(/(\r\n|\n|\r)/gm, "");
        text = text.replace(/(<([^>]+)>)/gi, " "); // strip html tags (a space, so headings and paragraphs don't run together)
        text = text.replace(/\s+/g, ' ').trim();
        text = text.substr(0, 200);
        text += "...";

        return text;
    }

    getCurrentEntry () {
        const title = this.titleInput.value;

        return {
            slug : this.getSlug(),
            language : this.languageSelector.value,
            title : title,
            preview : this.generatePreview(this.editor.value),
            date : this.dateInput.value + ":00Z",
            html : this.editor.value
        };
    }

    // an entry's text in every language, this one replaced
    withLanguage (value, language, text) {
        let texts = {};

        if (value !== null && typeof value === "object") {
            texts = Object.assign({}, value);
        } else if (typeof value === "string") {
            texts[seger.getDefaultLanguage()] = value;
        }
        texts[language] = text;

        return texts;
    }

    generateEntry () {
        this.saveDraft();

        const htmlContainer = document.getElementById('post-html');
        const summaryContainer = document.getElementById('post-summary');

        const entry = this.getCurrentEntry();
        const existing = seger.config.entries.find(e => e.slug === entry.slug) || {};

        summaryContainer.innerHTML = JSON.stringify({
            slug : entry.slug,
            title : this.withLanguage(existing.title, entry.language, entry.title),
            preview : this.withLanguage(existing.preview, entry.language, entry.preview),
            date : entry.date,
        }, null, 2);

        htmlContainer.innerHTML = entry.html;

        document.querySelector('[for="post-html"]').innerText = "posts/" + entry.slug + "/index." + entry.language + ".html";

        document.getElementById('output').classList.remove("d-none");
    }
}
window.blogEditor = new BlogEditor();
