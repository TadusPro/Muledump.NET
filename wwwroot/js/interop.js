function getElementWidth(element) {
    if (element) {
        return element.clientWidth;
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
        dotNetObjectReference.invokeMethodAsync('OnElementResized', document.body.clientWidth);
    }, 50);

    const observer = new ResizeObserver(debouncedCallback);
    observer.observe(document.body);

    observers.set(element, observer);

    debouncedCallback();
}

function unobserveResize(element) {
    const observer = observers.get(element);
    if (observer) {
        observer.disconnect();
        observers.delete(element);
    }
}

// FIX: stop relying on .account-list; use body width
function getWidth() {
    return document.body ? document.body.clientWidth : 0;
}

// Masonry helpers
const masonryInstances = new Map();
const masonryResizeHandlers = new Map();

/**
 * Initialize Masonry on a container that holds .mule tiles.
 * @param {string} selector - CSS selector for the container (e.g., ".account-grid")
 * @param {number} gutter - pixels between columns (e.g., 8)
 */
function initMasonry(selector, gutter = 0) { // gutter param ignored if gutter-sizer exists
    const container = document.querySelector(selector);
    if (!container) return;

    const tiles = container.querySelectorAll('.mule-item');
    if (tiles.length === 0) return;

    container.style.width = '100%';
    container.style.margin = '0';

    destroyMasonry(selector);

    const hasGridSizer = !!container.querySelector('.grid-sizer');
    const hasGutterSizer = !!container.querySelector('.gutter-sizer');

    const msnry = new Masonry(container, {
        itemSelector: '.mule-item',
        // 1 column == grid-sizer width (var(--card-width))
        columnWidth: hasGridSizer ? '.grid-sizer' : '.mule-item',
        // horizontal gap driven by element width (consistent math)
        gutter: hasGutterSizer ? '.gutter-sizer' : 0,
        fitWidth: false,
        percentPosition: false,
        originLeft: true,
        originTop: true,
        transitionDuration: '0.2s'
    });

    masonryInstances.set(selector, msnry);

    const relayout = debounce(() => msnry.layout(), 50);
    window.addEventListener('resize', relayout);
    masonryResizeHandlers.set(selector, relayout);

    msnry.layout();
}

function relayoutMasonry(selector) {
    const msnry = masonryInstances.get(selector);
    if (msnry) {
        msnry.layout();
    }
}

function destroyMasonry(selector) {
    const msnry = masonryInstances.get(selector);
    if (msnry) {
        msnry.destroy();
        masonryInstances.delete(selector);
    }
    const handler = masonryResizeHandlers.get(selector);
    if (handler) {
        window.removeEventListener('resize', handler);
        masonryResizeHandlers.delete(selector);
    }
}

function refreshMasonry(selector, gutter = 8) {
    const container = document.querySelector(selector);
    if (!container) return;
    destroyMasonry(selector);
    initMasonry(selector, gutter);
}

function getCssVar(el, name) {
    if (!el) return '';
    const v = getComputedStyle(el).getPropertyValue(name);
    return (v || '').trim();
}

function debugMasonry(selector, maxItems = 50) {
    const container = document.querySelector(selector);
    if (!container) return `[debug] container ${selector} not found`;

    const lines = [];
    const contRect = container.getBoundingClientRect();
    lines.push(`[container] clientWidth=${container.clientWidth} offsetWidth=${container.offsetWidth} rectW=${Math.round(contRect.width)}`);
    lines.push(`[vars] --card-width=${getCssVar(container, '--card-width') || '(unset)'}`);

    const colSizer = container.querySelector('.grid-sizer');
    if (colSizer) lines.push(`[grid-sizer] offsetWidth=${colSizer.offsetWidth}`);

    const gutSizer = container.querySelector('.gutter-sizer');
    if (gutSizer) lines.push(`[gutter-sizer] offsetWidth=${gutSizer.offsetWidth}`);

    const items = Array.from(container.querySelectorAll('.mule-item'));
    lines.push(`[items] count=${items.length}`);

    items.slice(0, maxItems).forEach((item, i) => {
        const iRect = item.getBoundingClientRect();
        const mule = item.querySelector('.mule');
        const mRect = mule ? mule.getBoundingClientRect() : { width: 0 };
        const chars = mule ? mule.querySelector('.chars') : null;
        const rowLen = chars ? (chars.style.getPropertyValue('--char-row-length') || getCssVar(chars, '--char-row-length')) : '';
        const cardVar = mule ? getCssVar(mule, '--card-width') : '';
        lines.push(`[item ${i}] itemW=${Math.round(iRect.width)} muleW=${Math.round(mRect.width)} rowLen=${rowLen || '(n/a)'} cardVar=${cardVar || '(n/a)'} `);
    });

    return lines.join('\n');
}

// Expose API
window.mdt = window.mdt || {};
window.mdt.masonry = {
    init: initMasonry,
    relayout: relayoutMasonry,
    destroy: destroyMasonry,
    refresh: refreshMasonry,
    debug: debugMasonry
};