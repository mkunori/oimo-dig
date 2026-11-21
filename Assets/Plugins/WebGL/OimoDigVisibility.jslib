mergeInto(LibraryManager.library, {
    $OimoDigVisibilityState: { revision: 0, saveOnHide: null, saveOnPageHide: null },

    OimoDigGetPageVisibility__deps: ["$OimoDigVisibilityState"],
    OimoDigGetPageVisibility: function () {
        if (OimoDigVisibilityState.revision === 0) {
            OimoDigVisibilityState.revision = 1;
            document.addEventListener("visibilitychange", function () {
                ++OimoDigVisibilityState.revision;
            });
        }

        // The revision also detects a hide/show cycle that happened between frames.
        var revision = OimoDigVisibilityState.revision;
        return document.hidden ? -revision : revision;
    },

    OimoDigSetPageSaveCallback__deps: ["$OimoDigVisibilityState"],
    OimoDigSetPageSaveCallback: function (callback) {
        var state = OimoDigVisibilityState;
        if (state.saveOnHide) {
            document.removeEventListener("visibilitychange", state.saveOnHide);
            window.removeEventListener("pagehide", state.saveOnPageHide);
            state.saveOnHide = state.saveOnPageHide = null;
        }
        if (!callback) return;

        state.saveOnPageHide = function () {
            {{{ makeDynCall('v', 'callback') }}}();
        };
        state.saveOnHide = function () {
            if (document.hidden) state.saveOnPageHide();
        };
        document.addEventListener("visibilitychange", state.saveOnHide);
        window.addEventListener("pagehide", state.saveOnPageHide);
    }
});
