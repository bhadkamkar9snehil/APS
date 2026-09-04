(() => {
    const isMasterDataDelete = (button) => {
        if (!(button instanceof HTMLButtonElement)) return false;
        if (window.location.pathname.replace(/\/$/, "") !== "/plan/master-data") return false;
        return button.textContent?.trim().toLowerCase() === "delete";
    };

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) return;

        const button = target.closest("button");
        if (!isMasterDataDelete(button)) return;

        const confirmed = window.confirm(
            "Delete this master-data record? This action cannot be undone and may affect planning feasibility."
        );

        if (!confirmed) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);
})();
