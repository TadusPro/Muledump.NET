function getElementWidth(element) {
    if (element) {
        return element.clientWidth || element.getBoundingClientRect().width || 0;
    }
    return 0;
}

const observers = new Map();

function debounce(func, delay) {
    let timeout;
    return function (...args) {
        clearTimeout(timeout);
        timeout = setTimeout(() => func.apply(this, args), delay);
    };
}

function observeResize(dotNetObjectReference, element) {
    if (!element) return;

    const debouncedCallback = debounce(() => {
        const w = element.clientWidth || element.getBoundingClientRect().width || 0;
        dotNetObjectReference.invokeMethodAsync('OnElementResized', w);
    }, 50);

    const observer = new ResizeObserver(debouncedCallback);
    observer.observe(element);

    observers.set(element, observer);

    // Initial call
    debouncedCallback();
}

function unobserveResize(element) {
    const observer = observers.get(element);
    if (observer) {
        observer.disconnect();
        observers.delete(element);
    }
}

function getWidth() {
    const accountList = document.querySelector('.account-list');
    return accountList ? accountList.clientWidth : 0;
}