// Lukker "Mere"-menuen på mobil, når man trykker på et link eller uden for menuen.
document.addEventListener("click", function (e) {
    var d = document.querySelector("details.more[open]");
    if (!d) return;
    if (!d.contains(e.target) || e.target.closest("a.more-link")) d.open = false;
});

// Tabeller bliver til kort på mobil: hver celle får kolonnens overskrift som label.
(function () {
    function maerk(tabel) {
        var hoveder = Array.prototype.map.call(tabel.querySelectorAll("thead th"), function (th) { return th.textContent.trim(); });
        Array.prototype.forEach.call(tabel.querySelectorAll("tbody tr"), function (tr) {
            Array.prototype.forEach.call(tr.children, function (td, i) {
                var label = hoveder[i] || "";
                if (label && td.getAttribute("data-label") !== label) td.setAttribute("data-label", label);
            });
        });
    }
    function alle() { document.querySelectorAll("table.table").forEach(maerk); }
    var planlagt = false;
    new MutationObserver(function () {
        if (planlagt) return;
        planlagt = true;
        requestAnimationFrame(function () { planlagt = false; alle(); });
    }).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener("DOMContentLoaded", alle);
})();
