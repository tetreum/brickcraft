## Languages

The game is played in English or Spanish (Settings → Language; the computer's language the first time, if the game has it). Texts are in Unity's Localization package (`com.unity.localization`): the locales and string tables of `Assets/Localization`, each table a key → text per language. Spanish falls back to English for anything it hasn't.

| Table | What's in it |
|---|---|
| `UI` | Menus and panels. |
| `Game` | What happens while playing: the server's messages, command replies, chat events, refusals. |
| `Controls` | Actions, their categories and key names (Settings → Controls). |
| `Items` | Names of the game's items (`item.[id]`) and NPCs (`npc.[id]`). LDraw parts and brick colours keep LEGO's names. |

Keys are `area.words` (`settings.language`, `cmd.usage_kick`). Texts with arguments use `{0}`, `{1}`... and Smart Strings for plurals: `{0} {0:plural:mod|mods}`.

### In code

- `Loc.Get(Loc.UI, "settings.language")`, or with arguments `Loc.Get(Loc.UI, "worlds.mod_count", count)`. A key the table hasn't is shown as itself, with a warning. Main thread only.
- When the language changes, `EventManager.LanguageChanged` is raised once its texts are loaded: whatever can be on screen then says its texts again.
- Static texts of the UI prefabs have a `LocalizedText` (a key of the UI table); menu buttons a `labelKey`. Brickcraft → Localize UI texts gives the prefabs' new static texts their keys (texts that scripts fill in are theirs to localize).
- The server never sends finished sentences: it sends a `GameText` (a key of the Game table and its arguments, `GameTexts.Send(conn, "cmd.teleported", name)`), and each player reads it in its own language. Items, NPCs and colours passed as arguments are named by the reader; numbers stay numbers for plurals; `GameText.Of("word.on")` is a word to translate. Names that travel as plain text (a killer, a storage's title) go as `GameText.Reference(...)` and are read with `GameText.Resolve`.
- `item.DisplayName` and `npcInfo.DisplayName` are their names in the player's language; `name` stays the English one (searches, logs).

### Translating

Brickcraft → Localization → Export JSON writes every table as `Localization/[table].[language].json` in the project's folder (`{ "key": "text" }`), and Import JSON reads them back (new keys are added, texts with Smart formats are made Smart). The Localization package's own Tables window and CSV export work too.

To add a language: create its locale (Window → Asset Management → Localization Tables, or the Localization settings), add it to `GameSettings.LanguageCodes` and `LanguageNames` (and `SystemLanguage` if computers in it should start in it), export the tables, translate, import. The Edit Mode tests fail while any text is missing in a language (`EveryTextIsInEveryLanguage`), or doesn't say its arguments (`EveryTextWithArgumentsSaysThemInEveryLanguage`), and while code uses a key no table has.

Mods translate their own names and texts in `lang/[language].json` (see Mods).
