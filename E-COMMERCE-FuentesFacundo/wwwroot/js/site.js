// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener('DOMContentLoaded', function () {
    const body = document.body;
    const toggle = document.getElementById('sidebarToggle');
    const closeButton = document.getElementById('sidebarClose');
    const overlay = document.getElementById('sidebarOverlay');

    function openSidebar() {
        body.classList.add('sidebar-open');
    }

    function closeSidebar() {
        body.classList.remove('sidebar-open');
    }

    if (toggle) {
        toggle.addEventListener('click', openSidebar);
    }

    if (closeButton) {
        closeButton.addEventListener('click', closeSidebar);
    }

    if (overlay) {
        overlay.addEventListener('click', closeSidebar);
    }
});

document.addEventListener('DOMContentLoaded', function () {
    const root = document.documentElement;
    const toggle = document.getElementById('themeToggle');

    function actualizarBotonTema() {
        if (!toggle) {
            return;
        }

        const oscuro = root.getAttribute('data-bs-theme') === 'dark';
        const etiqueta = oscuro ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro';

        toggle.setAttribute('aria-label', etiqueta);
        toggle.setAttribute('title', etiqueta);
        toggle.setAttribute('aria-pressed', oscuro ? 'true' : 'false');
    }

    actualizarBotonTema();

    if (toggle) {
        toggle.addEventListener('click', function () {
            const nuevoTema = root.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
            root.setAttribute('data-bs-theme', nuevoTema);

            try {
                localStorage.setItem('theme', nuevoTema);
            } catch (e) {
            }

            actualizarBotonTema();
        });
    }
});
