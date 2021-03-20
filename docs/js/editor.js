class BlogEditor {
    constructor () {
        this.loadedInterval = setInterval(() => {
            if (typeof Jodit === "undefined") {
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
        this.entrySelector = document.getElementById('existing-entry-selector');
        this.previewButton = document.getElementById('preview');
        
        this.dateInput.value = this.getTodayDate();
        
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
            this.previewButton.href = "?/post/" + this.slugify(this.titleInput.value);
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
    
    getTodayDate () {
        return new Date().toISOString().slice(0, 16);
    }
    
    saveDraft () {
        localStorage.setItem("draft", JSON.stringify(this.getCurrentEntry()));
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
            
        option.innerText = entry.title;
        option.value = entry.slug;

        if (prepend) {
            this.entrySelector.prepend(option);
        } else {
            this.entrySelector.appendChild(option);
        }
    }
    
    recoverEntry () {
        if (this.entrySelector.value.length === 0) {
            this.applyEntry({
                html: '',
                title: '',
                date: this.getTodayDate(),
            });
        } else {
            seger.getFullEntry(this.entrySelector.value).then(entry => {
                this.applyEntry(entry);
            });
        }
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
        
        this.applyEntry(draft);
        
        draft.title = "Draft: " + draft.title;
        this.addEntryOption(draft, true);
    }
    
    // from https://gist.github.com/codeguy/6684588
    slugify (value) {
        return value
            .normalize('NFD') // split an accented letter in the base letter and the acent
            .replace(/[\u0300-\u036f]/g, '') // remove all previously split accents
            .toLowerCase()
            .trim()
            .replace(/[^a-z0-9 ]/g, '') // remove all chars not letters, numbers and spaces (to be replaced)
            .replace(/\s+/g, '-') // separator
    }
    
    generatePreview (text) {
        text = text.replace(/(\r\n|\n|\r)/gm, "").replace(/\s+/g,' ').trim();
        text = text.replace(/(<([^>]+)>)/gi, "").trim(); // strip html tags
        text = text.substr(0, 200);
        text += "...";
        
        return text;
    }
    
    getCurrentEntry () {
        const title = this.titleInput.value;
        
        return {
            slug : this.slugify(title),
            title : title,
            preview : this.generatePreview(this.editor.value),
            date : this.dateInput.value + ":00Z",
            html : this.editor.value
        };
    }
                    
    generateEntry () {
        this.saveDraft();
        
        const htmlContainer = document.getElementById('post-html');
        const summaryContainer = document.getElementById('post-summary');
        
        const entry = this.getCurrentEntry();
        
        summaryContainer.innerHTML = JSON.stringify({
            slug : entry.slug,
            title : entry.title,
            preview : entry.preview,
            date : entry.date,
        }, null, 2);
        
        htmlContainer.innerHTML = entry.html;
        
        document.querySelector('[for="post-html"]').innerText = "posts/" + entry.slug + "/index.html";
        
        document.getElementById('output').classList.remove("d-none");
    }
}
window.blogEditor = new BlogEditor();