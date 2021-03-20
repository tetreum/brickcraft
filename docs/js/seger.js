class Seger {
                
    constructor () {
        this.mainContainer = document.getElementById('main-container');

        this.getConfig().then(() => {
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
    
    getLanguage = () => {
        return navigator.userLanguage || (navigator.languages && navigator.languages.length && navigator.languages[0]) || navigator.language || navigator.browserLanguage || navigator.systemLanguage || 'en';
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
        return new Promise((resolve, reject) => {
            const count = this.config.entries.length - 1;
            
            this.config.entries.forEach((entry, i) => {
                if (entry.slug === slug) {
                    return resolve(entry);
                }
                // requested entry not found
                if (i === count) {
                    reject();
                }
            });
        });
    }
    
    getFullEntry (slug) {
        return new Promise((resolve, reject) => {
            this.getEntry(slug).then((entry) => {
                if (typeof entry.html !== 'undefined') {
                    resolve(entry);
                    return;
                }
                fetch('posts/' + slug + "/index.html")
                    .then(response => response.text())
                    .then(html => {
                        html = html.replace(/src="((?!http).+)"/, 'src="posts/' + slug + '/$1"');
                        return html;
                    })
                    .then(html => {
                        entry.html = html;
                        resolve(entry);
                    });
            }).catch(() => {
                const draft = this.getDraft();
                
                if (draft === null || draft.slug !== slug) {
                    return reject();
                }
                resolve(draft);
            });
        });
    }
    
    getDraft () {
        let draft = localStorage.getItem("draft");
        
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
            
            this.render(this.parseTemplate("post", {entry, relatedPosts}));
            this.setTitle(entry.title);
            this.setMetas([
                {
                    property: "og:title",
                    content: entry.title,
                },
                {
                    name: "twitter:title",
                    content: entry.title,
                },
                {
                    name: "description",
                    content: entry.preview,
                },
                {
                    name: "twitter:description",
                    content: entry.preview,
                },
                {
                    property: "og:description",
                    content: entry.preview,
                }
            ]);
        }).catch(() => {
            this.show404();
        });
    }
    
    showHome () {
        this.setTitle("Home");
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
        this.setTitle("Page not found");
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
        // remove previous entries
        document.querySelectorAll('meta').forEach(el => {
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