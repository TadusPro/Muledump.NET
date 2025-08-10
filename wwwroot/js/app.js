function adjustDropdown(menuElement) {
    if (!menuElement) {
        return;
    }

    // Look for either options-dropdown or reload-dropdown
    const dropdown = menuElement.querySelector('.options-dropdown') || menuElement.querySelector('.reload-dropdown');
    if (!dropdown) {
        return;
    }

    // Reset style to default for accurate measurement
    dropdown.style.left = '0px';

    const rect = dropdown.getBoundingClientRect();
    const viewportWidth = window.innerWidth;

    if (rect.right > viewportWidth) {
        // Get the computed style to read the box-shadow property
        const style = window.getComputedStyle(dropdown);
        const boxShadow = style.boxShadow;
        let shadowSpread = 0;

        // A simple parser to find the spread radius (the 4th length value)
        if (boxShadow && boxShadow !== 'none') {
            // This regex finds color values (rgb, rgba, hsl, hsla, hex)
            const colorRegex = /(rgba?\(|hsla?\(|#)([^\)]*)\)?/g;
            // Remove color definitions to safely split by space
            const shadowValues = boxShadow.replace(colorRegex, '').trim().split(' ');
            // The spread radius is the 4th value if it exists and is a length
            if (shadowValues.length >= 4) {
                shadowSpread = parseFloat(shadowValues[3]) || 0;
            }
        }

        // Calculate how much the dropdown is overflowing, including the shadow
        const overflow = rect.right - viewportWidth + shadowSpread;
        // Apply a negative left style to shift it back into view
        dropdown.style.left = `-${overflow}px`;
    }
}