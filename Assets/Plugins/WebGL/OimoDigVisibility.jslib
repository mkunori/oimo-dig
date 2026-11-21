mergeInto(LibraryManager.library, {
    $OimoDigVisibilityState: { revision: 0 },

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
    }
});
