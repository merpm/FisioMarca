document.addEventListener("DOMContentLoaded", () => {
    // ===============================
    // THEME TOGGLE (DARK/LIGHT)
    // ===============================
    const THEME_KEY = "fm_admin_theme";
    const themeBtn = document.getElementById("themeToggle");

    const getTheme = () => document.documentElement.getAttribute("data-theme") || "dark";

    const syncThemeButton = (theme) => {
        if (!themeBtn) return;
        const isDark = theme === "dark";
        themeBtn.classList.toggle("is-dark", isDark);
        themeBtn.classList.toggle("is-light", !isDark);
        themeBtn.setAttribute("aria-label", isDark ? "Cambiar a modo claro" : "Cambiar a modo oscuro");
    };

    const applyChartDefaults = (theme) => {
        if (!window.Chart) return;

        const isDark = theme === "dark";
        const textColor = isDark ? "#e5e7eb" : "#0f172a";
        const gridColor = isDark ? "rgba(148,163,184,0.18)" : "rgba(15,23,42,0.12)";

        try {
            Chart.defaults.color = textColor;
            Chart.defaults.borderColor = gridColor;
            if (Chart.defaults.plugins?.legend?.labels) {
                Chart.defaults.plugins.legend.labels.color = textColor;
            }
        } catch { }
    };

    const setTheme = (theme) => {
        document.documentElement.setAttribute("data-theme", theme);
        localStorage.setItem(THEME_KEY, theme);
        syncThemeButton(theme);
        applyChartDefaults(theme);
    };

    const savedTheme = localStorage.getItem(THEME_KEY);
    if (savedTheme) document.documentElement.setAttribute("data-theme", savedTheme);
    else if (!document.documentElement.getAttribute("data-theme")) document.documentElement.setAttribute("data-theme", "dark");

    syncThemeButton(getTheme());
    applyChartDefaults(getTheme());

    const isDashboardPage =
        !!document.getElementById("btnRefreshDashboard") ||
        !!document.getElementById("adminCalendar") ||
        !!document.getElementById("chartByDay") ||
        !!document.getElementById("chartTopServices");

    if (themeBtn) {
        themeBtn.addEventListener("click", () => {
            const next = getTheme() === "dark" ? "light" : "dark";
            setTheme(next);

            if (isDashboardPage) refreshDashboard();
        });
    }

    if (!isDashboardPage) return;

    // ===============================
    // TU DASHBOARD
    // ===============================
    const btnRefresh = document.getElementById("btnRefreshDashboard");
    const refreshInfo = document.getElementById("refreshInfo");

    const elTotal = document.getElementById("statTotal");
    const elProg = document.getElementById("statProgramadas");
    const elAt = document.getElementById("statAtendidas");
    const elCan = document.getElementById("statCanceladas");
    const elTasa = document.getElementById("statTasa");

    let calendar = null;
    let chartByDay = null;
    let chartTop = null;

    // ===============================
    // MODAL DETALLE (BOOTSTRAP)
    // ===============================
    const modalEl = document.getElementById("reservationDetailModal");
    const modal = () => (modalEl && window.bootstrap) ? bootstrap.Modal.getOrCreateInstance(modalEl) : null;

    const fmtDateTime = (d) => {
        if (!d) return "-";
        return new Date(d).toLocaleString("es-PE", {
            day: "2-digit", month: "2-digit", year: "numeric",
            hour: "2-digit", minute: "2-digit"
        });
    };

    const fmtMoney = (val) => {
        const n = Number(val);
        if (Number.isNaN(n)) return "-";
        return `S/ ${n.toFixed(2)}`;
    };

    function setStatusChip(text) {
        const el = document.getElementById("rdStatus");
        if (!el) return;

        const st = (text || "-").toString().trim();
        const k = st.toLowerCase();

        el.textContent = st;
        el.className = "alert-status";
        if (k.includes("program")) el.classList.add("programada");
        else if (k.includes("atend")) el.classList.add("atendida");
        else if (k.includes("cancel")) el.classList.add("cancelada");
    }

    function openReservationModal(fcEvent) {
        if (!fcEvent) return;

        const p = fcEvent.extendedProps || {};

        const elService = document.getElementById("rdService");
        const elClient = document.getElementById("rdClient");
        const elDate = document.getElementById("rdDate");
        const elPrice = document.getElementById("rdPrice");
        const elComment = document.getElementById("rdComment");

        if (elService) elService.textContent = p.service ?? "-";
        if (elClient) elClient.textContent = p.client ?? "-";
        if (elDate) elDate.textContent = fmtDateTime(fcEvent.start);
        setStatusChip(p.status ?? "-");
        if (elPrice) elPrice.textContent = fmtMoney(p.price);
        if (elComment) elComment.textContent = p.comment ?? "";

        const m = modal();
        if (m) m.show();
    }

    const fmtNow = () => {
        const d = new Date();
        const pad = (n) => String(n).padStart(2, "0");
        return `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
    };

    const setInfo = (msg) => { if (refreshInfo) refreshInfo.textContent = msg || ""; };

    function getRangeParams() {
        const rangeSel = document.querySelector('select[name="range"]');
        const fromInp = document.querySelector('input[name="from"]');
        const toInp = document.querySelector('input[name="to"]');

        const range = rangeSel ? (rangeSel.value || "month") : "month";
        const from = fromInp ? (fromInp.value || "") : "";
        const to = toInp ? (toInp.value || "") : "";

        const qs = new URLSearchParams();
        qs.set("range", range);
        if (from) qs.set("from", from);
        if (to) qs.set("to", to);
        return qs.toString();
    }

    async function fetchDashboardData() {
        const qs = getRangeParams();
        const url = "/Admin/DashboardData" + (qs ? `?${qs}` : "");

        const res = await fetch(url, { headers: { "Accept": "application/json" } });
        if (!res.ok) throw new Error(`DashboardData error: ${res.status}`);
        return await res.json();
    }

    function renderCalendar(events) {
        const calEl = document.getElementById("adminCalendar");
        if (!calEl || !window.FullCalendar) return;

        if (!calendar) {
            calendar = new FullCalendar.Calendar(calEl, {
                locale: "es",
                initialView: "dayGridMonth",
                height: "auto",
                headerToolbar: { left: "prev,next today", center: "title", right: "dayGridMonth,timeGridWeek,timeGridDay" },
                events: events || [],

                // ✅ CLICK EVENTO -> MODAL
                eventClick: function (info) {
                    info.jsEvent.preventDefault();
                    openReservationModal(info.event);
                }
            });

            calendar.render();
        } else {
            calendar.removeAllEvents();
            (events || []).forEach((ev) => calendar.addEvent(ev));
        }
    }

    function getChartTheme() {
        const isDark = getTheme() === "dark";
        return {
            textColor: isDark ? "#e5e7eb" : "#0f172a",
            gridColor: isDark ? "rgba(148,163,184,0.18)" : "rgba(15,23,42,0.12)"
        };
    }

    function renderCharts(byDay, topServices) {
        const { textColor, gridColor } = getChartTheme();

        const byDayEl = document.getElementById("chartByDay");
        if (byDayEl && window.Chart) {
            if (chartByDay) chartByDay.destroy();
            chartByDay = new Chart(byDayEl, {
                type: "bar",
                data: {
                    labels: (byDay?.labels || []),
                    datasets: [{ label: "Reservas", data: (byDay?.values || []) }],
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { labels: { color: textColor } } },
                    scales: {
                        x: { ticks: { color: textColor }, grid: { color: gridColor } },
                        y: { ticks: { color: textColor }, grid: { color: gridColor } }
                    }
                },
            });
        }

        const topEl = document.getElementById("chartTopServices");
        if (topEl && window.Chart) {
            if (chartTop) chartTop.destroy();
            chartTop = new Chart(topEl, {
                type: "doughnut",
                data: {
                    labels: (topServices?.labels || []),
                    datasets: [{ label: "Top", data: (topServices?.values || []) }],
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { labels: { color: textColor } } }
                },
            });
        }
    }

    function renderStats(stats) {
        if (!stats) return;
        if (elTotal) elTotal.textContent = stats.total ?? 0;
        if (elProg) elProg.textContent = stats.programadas ?? 0;
        if (elAt) elAt.textContent = stats.atendidas ?? 0;
        if (elCan) elCan.textContent = stats.canceladas ?? 0;
        if (elTasa) elTasa.textContent = `${stats.tasaCancelacion ?? 0}%`;
    }

    async function refreshDashboard() {
        try {
            if (btnRefresh) btnRefresh.disabled = true;
            setInfo("Actualizando...");

            const data = await fetchDashboardData();
            renderStats(data.stats);
            renderCalendar(data.events);       // ahora trae extendedProps
            renderCharts(data.byDay, data.topServices);

            setInfo(`Actualizado: ${fmtNow()}`);
        } catch (e) {
            console.error(e);
            setInfo("Error al actualizar (revisa consola).");
        } finally {
            if (btnRefresh) btnRefresh.disabled = false;
        }
    }

    if (btnRefresh) btnRefresh.addEventListener("click", refreshDashboard);

    refreshDashboard();
});