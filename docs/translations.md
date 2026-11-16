# Contributing translations

The embedded page keeps its translations in `src/EmbyWatchTogether/Web/Configuration/watchtogether.js`. Chinese source strings are the keys and `translations.en` contains English values. Static HTML text and attributes are translated when the page is initialized; dynamic labels, messages, and dialog text use `t()` when created. User-supplied room and media names are not translation keys.

To add a language:

1. Add a locale dictionary alongside `translations.en`, using the same complete set of Chinese keys. Preserve placeholders, punctuation and any HTML semantics in context; inspect each use rather than translating fragments in isolation. Missing entries fall back to the Chinese source string.
2. Extend `currentLocale()` to map the desired language tags to the new dictionary. It first tries the host's `globalize.getCurrentLocale()`, then `data-culture`, the document's `lang`, and the browser language. Unknown languages currently use English. The controller checks the language whenever the page resumes; reopen or refresh after changing the host language.
3. Add UI tests in `tests/ui/specs/watchtogether.spec.js` for the new locale, including dynamic states and dialogs. Run `npm ci` and `npm test` in `tests/ui`. Check the page in a real Emby host where possible.
4. Update the interface-language sections of both READMEs and submit the translation for review. When embedded HTML or JavaScript changes, update the page/controller cache keys and their tests together, as required by the repository's existing resource registration.

The page cannot translate notifications sent directly by the server to a player: the server-side session snapshot does not supply a reliable recipient UI language. Keep those messages and synchronization behavior separate from page translation changes.

The current first version keeps the dictionary in the controller and uses Chinese source text as keys. Some dynamic sentences are assembled from translated fragments; review those combinations in context when adding a language. A separate resource file, semantic message IDs, and complete parameterized messages would make larger translation sets easier to maintain, but require a coordinated resource-loading and migration change; they are not part of this initial version.
