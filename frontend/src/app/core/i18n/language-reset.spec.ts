// Global Jasmine hook (top level, outside any describe): runs before every spec in the suite.
// I18nService persists the chosen language in localStorage, so a spec that switches to English
// without cleaning up would leak it into whichever spec runs next in the randomized order.
beforeEach(() => {
  localStorage.removeItem('bpms_language');
});
