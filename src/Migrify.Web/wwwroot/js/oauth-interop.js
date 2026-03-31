window.migrifyOAuth = {
    _listener: null,

    openPopup: function (url) {
        var w = 500, h = 650;
        var left = (screen.width - w) / 2;
        var top = (screen.height - h) / 2;
        window.open(url, 'oauth_popup',
            'width=' + w + ',height=' + h + ',left=' + left + ',top=' + top + ',scrollbars=yes');
    },

    registerCallback: function (dotNetRef) {
        // Remove previous listener if any
        if (this._listener) {
            window.removeEventListener('message', this._listener);
        }

        this._listener = function (event) {
            if (event.data && event.data.type === 'oauth-callback') {
                dotNetRef.invokeMethodAsync('OnOAuthCallback',
                    event.data.success,
                    event.data.jobId || '',
                    event.data.error || '');
            }
        };

        window.addEventListener('message', this._listener);
    },

    unregisterCallback: function () {
        if (this._listener) {
            window.removeEventListener('message', this._listener);
            this._listener = null;
        }
    }
};
