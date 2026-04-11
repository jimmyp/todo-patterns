// TodoList.Web/Client/wwwroot/js/connectivity.js
let dotNetRef = null;

export function initialize(ref) {
    dotNetRef = ref;

    window.addEventListener('online', () => {
        if (dotNetRef) dotNetRef.invokeMethodAsync('OnConnectivityChanged', true);
    });

    window.addEventListener('offline', () => {
        if (dotNetRef) dotNetRef.invokeMethodAsync('OnConnectivityChanged', false);
    });

    return navigator.onLine;
}

export function isOnline() {
    return navigator.onLine;
}

export function dispose() {
    dotNetRef = null;
}
