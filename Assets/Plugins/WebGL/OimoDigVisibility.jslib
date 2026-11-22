mergeInto(LibraryManager.library, {
    $OimoDigVisibilityState: { suspended: false, onVisibility: null, onPageHide: null, onPageShow: null },

    OimoDigGetPageVisibility__deps: ["$OimoDigVisibilityState"],
    OimoDigGetPageVisibility: function () {
        return document.hidden || OimoDigVisibilityState.suspended ? 0 : 1;
    },

    OimoDigSetPageVisibilityCallback__deps: ["$OimoDigVisibilityState"],
    OimoDigSetPageVisibilityCallback: function (callback) {
        var state = OimoDigVisibilityState;
        if (state.onVisibility) {
            document.removeEventListener("visibilitychange", state.onVisibility);
            window.removeEventListener("pagehide", state.onPageHide);
            window.removeEventListener("pageshow", state.onPageShow);
            state.onVisibility = state.onPageHide = state.onPageShow = null;
        }
        if (!callback) return;

        var notify = function (visible) {
            {{{ makeDynCall('vi', 'callback') }}}(visible);
        };
        state.onVisibility = function () {
            notify(document.hidden || state.suspended ? 0 : 1);
        };
        state.onPageHide = function () {
            state.suspended = true;
            notify(0);
        };
        state.onPageShow = function () {
            state.suspended = false;
            state.onVisibility();
        };
        document.addEventListener("visibilitychange", state.onVisibility);
        window.addEventListener("pagehide", state.onPageHide);
        window.addEventListener("pageshow", state.onPageShow);
    }
});
