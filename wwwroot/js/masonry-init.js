(function () {
    const state = new WeakMap();

    function calcAndSnapWidth(container, itemWidth, gutter) {
        container.style.position = 'relative';
        container.style.width = '100%';
        container.style.marginLeft = '0';
        container.style.marginRight = '0';
        container.style.setProperty('--masonry-vert-gap', `${gutter}px`);
        const avail = container.clientWidth || (container.parentElement?.clientWidth ?? 0);
        const cols = Math.max(1, Math.floor((avail + gutter) / (itemWidth + gutter)));
        return { cols, snapped: avail };
    }

    function init(container, options) {
        if (!container) return;
        const itemEl = container.querySelector('.mule');
        if (!itemEl) return;

        const gutter = options?.gutter ?? 10;

        // Base column width (1-col card minimum). Cards can be wider and will span multiple columns.
        const baseColumnWidth = 184;

        calcAndSnapWidth(container, baseColumnWidth, gutter);

        const msnry = new Masonry(container, {
            itemSelector: '.mule',
            columnWidth: baseColumnWidth,
            gutter: gutter,
            originLeft: true,
            percentPosition: false,
            transitionDuration: 0
        });

        const ro = new ResizeObserver(() => {
            calcAndSnapWidth(container, baseColumnWidth, gutter);
            msnry.layout();
        });
        ro.observe(container.parentElement || document.body);

        state.set(container, { msnry, ro, gutter, itemWidth: baseColumnWidth });

        requestAnimationFrame(() => msnry.layout());
    }

    function layout(container) {
        const s = state.get(container);
        if (!s) return init(container);
        calcAndSnapWidth(container, s.itemWidth, s.gutter);
        s.msnry.reloadItems();
        s.msnry.layout();
    }

    function destroy(container) {
        const s = state.get(container);
        if (!s) return;
        s.ro?.disconnect();
        s.msnry?.destroy();
        state.delete(container);
    }

    window.accountsMasonry = { init, layout, destroy };
})();