// Busca un control del HTML para leerlo, actualizarlo o registrar sus eventos.
function elemento(selector) {
    return document.querySelector(selector);
}

// Escapa texto variable antes de colocarlo en fragmentos de HTML.
function escaparHtml(valor) {
    let texto = String(valor ?? '');
    texto = texto.replaceAll('&', '&amp;');
    texto = texto.replaceAll('<', '&lt;');
    texto = texto.replaceAll('>', '&gt;');
    texto = texto.replaceAll('"', '&quot;');
    return texto.replaceAll("'", '&#39;');
}

// Presenta valores del portafolio en USD, o un aviso si falta precio.
function formatoMoneda(valor) {
    if (valor === null || valor === undefined) {
        return 'No disponible';
    }
    return new Intl.NumberFormat('es-MX', {
        style: 'currency',
        currency: 'USD',
    }).format(valor);
}

// Aplica el formato local y conserva la precisión de las cantidades fraccionarias.
function formatoNumero(valor) {
    return new Intl.NumberFormat('es-MX', { maximumFractionDigits: 8 }).format(
        valor,
    );
}

// Convierte los campos del formulario en un objeto para validar o enviar.
function leerFormulario(formulario) {
    const campos = new FormData(formulario);
    const datos = {};
    for (const campo of campos.entries()) {
        datos[campo[0]] = campo[1];
    }
    return datos;
}

let usuario = null,
    cuenta = null,
    estado = null,
    lecciones = [],
    cotizaciones = [];
let vista = 'inicio',
    simbolo = 'BTC',
    modo = localStorage.getItem('vantex-modo') || 'real';
let registro = true,
    cargandoMercado = false;
let operacionPendiente = null;
elemento('#market-mode').value = modo;

// Todas las peticiones usan rutas del servidor local, nunca claves de API.
// Hace las consultas a la API de Vantex y conserva los mensajes de error del servidor.
async function api(ruta, datos, leer) {
    const respuesta = await fetch(
        '/api' + ruta,
        datos === undefined
            ? {}
            : {
                  method: 'POST',
                  headers: {
                      'Content-Type': 'application/json',
                      'X-Vantex': '1',
                  },
                  body: JSON.stringify(datos),
              },
    );
    if (respuesta.ok && leer) return leer(respuesta);
    let contenido;
    try {
        contenido = await respuesta.json();
    } catch {
        contenido = {};
    }
    if (!respuesta.ok) {
        if (respuesta.status === 401) abrirAcceso();
        throw new Error(
            contenido.error ||
                (respuesta.status === 401
                    ? 'Inicia sesión para continuar.'
                    : respuesta.status === 429
                      ? 'Hay demasiadas solicitudes. Espera un minuto.'
                      : 'No se pudo completar la solicitud.'),
        );
    }
    return contenido;
}
// Muestra una confirmación temporal y cancela el temporizador del aviso anterior.
function aviso(texto, error = false) {
    elemento('#notification').textContent = texto;
    elemento('#notification').classList.toggle('error', error);
    elemento('#notification').hidden = false;
    clearTimeout(aviso.temporizador);
    aviso.temporizador = setTimeout(
        () => (elemento('#notification').hidden = true),
        9000,
    );
}
// Activa la vista elegida y carga sus datos al entrar por el menú o por el hash de la URL.
function navegar(destino) {
    vista = [
        'inicio',
        'mercados',
        'portafolio',
        'aprende',
        'metas',
        'finanzas',
        'tutor',
    ].includes(destino)
        ? destino
        : 'inicio';
    document
        .querySelectorAll('.view')
        .forEach((s) => (s.hidden = s.id !== 'view-' + vista));
    document
        .querySelectorAll('.nav')
        .forEach((b) => b.classList.toggle('active', b.dataset.view === vista));
    history.replaceState(null, '', '#' + vista);
    if (vista === 'mercados') actualizarSeccionMercado();
    if (vista === 'tutor') prepararTutor();
    if (vista === 'finanzas') cargarFinanzas();
    if (['inicio', 'portafolio', 'metas', 'aprende'].includes(vista))
        cargarCuenta();
    window.scrollTo({ top: 0, behavior: 'instant' });
}
document.querySelectorAll('[data-view]').forEach((b) =>
    b.addEventListener('click', (evento) => {
        evento.preventDefault();
        navegar(b.dataset.view);
    }),
);

// Abre el diálogo de cuenta sin duplicarlo si ya está en pantalla.
function abrirAcceso() {
    if (!elemento('#auth-dialog').open) elemento('#auth-dialog').showModal();
}
elemento('#auth-button').addEventListener('click', () => {
    if (usuario) {
        navegar('portafolio');
    } else abrirAcceso();
});
elemento('#close-auth').addEventListener('click', () =>
    elemento('#auth-dialog').close(),
);
elemento('#toggle-auth').addEventListener('click', () => {
    registro = !registro;
    elemento('#name-label').hidden = !registro;
    elemento('#auth-form').elements.nombre.required = registro;
    elemento('#auth-form').elements.password.autocomplete = registro
        ? 'new-password'
        : 'current-password';
    elemento('#auth-title').textContent = registro
        ? 'Comienza tu recorrido'
        : 'Bienvenido de nuevo';
    elemento('#auth-submit').textContent = registro
        ? 'Crear cuenta · 10,000 USD virtuales'
        : 'Iniciar sesión';
    elemento('#toggle-auth').textContent = registro
        ? 'Ya tengo cuenta · Iniciar sesión'
        : 'Soy nuevo · Crear cuenta';
    elemento('#auth-error').textContent = '';
});
elemento('#auth-form').addEventListener('submit', async (evento) => {
    evento.preventDefault();
    const formularioAcceso = evento.currentTarget;
    const boton = elemento('#auth-submit');
    boton.disabled = true;
    elemento('#auth-error').textContent = '';
    try {
        const datos = leerFormulario(formularioAcceso);
        const respuesta = await api(registro ? '/registro' : '/login', datos);
        const sesion = (await api('/sesion')).usuario;
        if (usuario?.id !== sesion?.id) reiniciarTutor();
        usuario = sesion;
        formularioAcceso.reset();
        elemento('#auth-dialog').close();
        actualizarSesion();
        if (vista === 'tutor') prepararTutor();
        await cargarCuenta();
        aviso(respuesta.mensaje);
    } catch (error) {
        elemento('#auth-error').textContent = error.message;
    } finally {
        boton.disabled = false;
    }
});
elemento('#logout').addEventListener('click', async () => {
    try {
        await api('/logout', {});
        usuario = null;
        cuenta = null;
        reiniciarTutor();
        actualizarSesion();
        dibujarCuenta();
        dibujarLecciones();
        aviso('Sesión cerrada.');
    } catch (e) {
        aviso(e.message, true);
    }
});
// Presenta el código solo dentro de la sesión web autenticada. La app lo canjea una vez.
elemento('#mobile-link-button')?.addEventListener('click', async (evento) => {
    const boton = evento.currentTarget;
    const salida = elemento('#mobile-link-code');
    boton.disabled = true;
    salida.hidden = false;
    try {
        const acceso = await api('/movil/codigo', {});
        salida.textContent = acceso.codigo + ' · Escríbelo en Conexión dentro de Vantex Móvil. Vence en '
            + acceso.minutos + ' minutos.';
    } catch (error) {
        salida.textContent = error.message;
    } finally {
        boton.disabled = false;
    }
});

// Actualiza los controles de sesión y oculta el código móvil al salir de la cuenta.
function actualizarSesion() {
    const vinculo = elemento('#mobile-link-panel');
    if (vinculo) {
        vinculo.hidden = !usuario || !estado?.movil;
        if (vinculo.hidden) {
            const codigo = elemento('#mobile-link-code');
            codigo.textContent = '';
            codigo.hidden = true;
        }
    }
    elemento('#auth-button').textContent = usuario
        ? usuario.nombre
        : 'Entrar / Crear cuenta';
    elemento('#logout').hidden = !usuario;
}
// Recupera el saldo y el avance del alumno autenticado.
async function cargarCuenta() {
    if (!usuario) {
        dibujarCuenta();
        dibujarLecciones();
        return;
    }
    try {
        cuenta = await api('/cuenta');
        dibujarCuenta();
        dibujarLecciones();
    } catch (e) {
        aviso(e.message, true);
    }
}
// Construye las tarjetas de saldo y resultados para el inicio y el portafolio.
function metricas() {
    return [
        [
            'Saldo disponible',
            cuenta ? formatoMoneda(cuenta.saldo) : '10,000 USD',
            'Virtuales al crear tu cuenta',
            '',
        ],
        [
            'Patrimonio virtual',
            cuenta ? formatoMoneda(cuenta.patrimonio) : '—',
            'Saldo + valor de las posiciones',
            '',
        ],
        [
            'Ganancia / pérdida abierta',
            cuenta ? formatoMoneda(cuenta.noRealizado) : '—',
            'Resultado aún no realizado',
            cuenta?.noRealizado < 0 ? 'negative' : 'positive',
        ],
        [
            'Resultado de ventas',
            cuenta ? formatoMoneda(cuenta.realizado) : '—',
            'Ganancia / pérdida realizada',
            cuenta?.realizado < 0 ? 'negative' : 'positive',
        ],
    ]
        .map(
            ([titulo, valor, nota, clase]) =>
                `<article class="metric"><label>${escaparHtml(titulo)}</label><strong class="${clase}">${escaparHtml(valor)}</strong><small>${escaparHtml(nota)}</small></article>`,
        )
        .join('');
}
// Presenta posiciones, compras y ventas, metas y estado de la cuenta.
function dibujarCuenta() {
    elemento('#metrics').innerHTML = metricas();
    elemento('#portfolio-metrics').innerHTML = metricas();
    if (!cuenta) {
        elemento('#holdings').innerHTML =
            '<p class="empty">Crea una cuenta para comenzar con 10,000 USD virtuales.</p>';
        elemento('#trades').innerHTML =
            '<p class="empty">Tus operaciones aparecerán aquí.</p>';
        elemento('#saved-goals').innerHTML =
            '<p class="empty">Inicia sesión para guardar tus metas.</p>';
        return;
    }
    elemento('#holdings').innerHTML = cuenta.posiciones.length
        ? `<table><thead><tr><th>ACTIVO / FUENTE</th><th>CANTIDAD</th><th>COSTO</th><th>VALOR ACTUAL</th><th>RESULTADO ABIERTO</th></tr></thead><tbody>${cuenta.posiciones.map((p) => `<tr><td><b>${escaparHtml(p.simbolo)}</b> <span class="badge">${p.modo === 'demo' ? 'DEMO' : 'MERCADO'}</span></td><td>${formatoNumero(p.cantidad)}</td><td>${formatoMoneda(p.costo)}</td><td>${formatoMoneda(p.valor)}</td><td class="${p.resultado < 0 ? 'negative' : 'positive'}">${formatoMoneda(p.resultado)}</td></tr>`).join('')}</tbody></table>`
        : '<p class="empty">Aún no tienes posiciones. Explora el simulador para practicar.</p>';
    elemento('#trades').innerHTML = cuenta.operaciones.length
        ? `<table><thead><tr><th>FECHA</th><th>OPERACIÓN</th><th>ACTIVO</th><th>CANTIDAD</th><th>PRECIO USD</th><th>TOTAL USD</th><th>RESULTADO VENTA</th><th>FUENTE</th></tr></thead><tbody>${cuenta.operaciones.map((o) => `<tr><td>${escaparHtml(new Date(o.fecha).toLocaleString('es-MX'))}</td><td class="${o.tipo === 'compra' ? 'positive' : ''}">${escaparHtml(o.tipo)}</td><td>${escaparHtml(o.simbolo)}</td><td>${formatoNumero(o.cantidad)}</td><td>${formatoMoneda(o.precio)}</td><td>${formatoMoneda(o.total)}</td><td>${o.tipo === 'venta' ? formatoMoneda(o.resultado) : '—'}</td><td>${escaparHtml(o.fuente)} · ${escaparHtml(o.modo)}</td></tr>`).join('')}</tbody></table>`
        : '<p class="empty">Aún no hay operaciones registradas.</p>';
    const contenedorMetas = elemento('#saved-goals');
    contenedorMetas.replaceChildren();
    if (cuenta.metas.length === 0) {
        contenedorMetas.textContent = 'Aún no tienes metas guardadas.';
    }
    for (const meta of cuenta.metas) {
        const tarjeta = document.createElement('article');
        tarjeta.className = 'saved-goal';
        const titulo = document.createElement('h3');
        titulo.textContent = meta.nombre;
        const detalle = document.createElement('p');
        detalle.textContent =
            'Objetivo: ' +
            formatoNumero(meta.costo) +
            ' · Aportación mensual: ' +
            formatoNumero(meta.mensual) +
            ' · Tasa hipotética: ' +
            formatoNumero(meta.tasaAnual) +
            ' %';
        const boton = document.createElement('button');
        boton.type = 'button';
        boton.className = 'text-button';
        boton.textContent = 'Ver escenario →';
        boton.addEventListener('click', function () {
            const formulario = elemento('#goal-form');
            formulario.elements.nombre.value = meta.nombre;
            formulario.elements.costo.value = meta.costo;
            formulario.elements.inicial.value = meta.inicial;
            formulario.elements.mensual.value = meta.mensual;
            formulario.elements.tasaAnual.value = meta.tasaAnual;
            navegar('metas');
            proyectar();
        });
        tarjeta.append(titulo, detalle, boton);
        contenedorMetas.append(tarjeta);
    }
}

let seccionMercado = 'quotes';
const seccionesMercado = elemento('#market-sections');

// Consulta el proveedor que corresponde a la sección de mercados seleccionada.
function actualizarSeccionMercado() {
    if (seccionMercado === 'quotes') cargarMercado();
    if (seccionMercado === 'cetes') cargarCetes();
}

seccionesMercado?.addEventListener('click', (evento) => {
    const boton = evento.target.closest('[data-market-section]');
    if (!boton) return;
    seccionMercado = boton.dataset.marketSection;
    seccionesMercado.querySelectorAll('button').forEach((opcion) => {
        const activa = opcion === boton;
        opcion.setAttribute('aria-pressed', activa);
        document.getElementById(opcion.getAttribute('aria-controls')).hidden = !activa;
    });
    elemento('#market-mode-label').hidden = seccionMercado !== 'quotes';
    actualizarSeccionMercado();
});

const panelAhorro = elemento('#market-savings-panel');
const formularioAhorro = elemento('#market-savings-form');
const resultadoAhorro = elemento('#market-savings-result');
panelAhorro?.addEventListener('click', (evento) => {
    const boton = evento.target.closest('[data-savings-name]');
    if (!boton) return;
    panelAhorro.querySelectorAll('[data-savings-name]').forEach((opcion) =>
        opcion.setAttribute('aria-pressed', opcion === boton),
    );
    elemento('#savings-selected').textContent = boton.dataset.savingsName + ' · ' + boton.dataset.savingsType;
    resultadoAhorro.replaceChildren();
});

// Presenta la ganancia bruta y el saldo final del simulador en pesos.
function mostrarRendimiento(contenedor, rendimiento, saldo) {
    contenedor.innerHTML = `<div><span>Rendimiento bruto</span><strong>${formatoMXN(rendimiento)}</strong></div><div><span>Saldo final estimado</span><strong>${formatoMXN(saldo)}</strong></div>`;
}

formularioAhorro?.addEventListener('input', () => resultadoAhorro.replaceChildren());
formularioAhorro?.addEventListener('submit', (evento) => {
    evento.preventDefault();
    const { capital, dias, tasa } = leerFormulario(formularioAhorro);
    const rendimiento = Number(capital) * Number(tasa) / 100 * Number(dias) / 365;
    mostrarRendimiento(resultadoAhorro, rendimiento, Number(capital) + rendimiento);
});

const listaCetes = elemento('#cetes-rates');
const formularioCetes = elemento('#market-cetes-form');
const resultadoCetes = elemento('#market-cetes-result');
const botonCetes = elemento('#refresh-cetes');
const formatoTasa = new Intl.NumberFormat('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 4 });
let tasasCetes = [];
let plazoCetes = 28;

// Actualiza la fecha, las tasas de Banxico y el estado de la última consulta.
async function cargarCetes() {
    if (!botonCetes || botonCetes.disabled) return;
    botonCetes.disabled = true;
    const estadoCetes = elemento('#cetes-status');
    if (!tasasCetes.length) estadoCetes.textContent = 'Consultando las tasas de Banxico…';
    try {
        const datos = await api('/cetes');
        tasasCetes = datos.tasas;
        if (!datos.fechaSubasta) {
            estadoCetes.textContent = 'No se pudieron obtener tasas de Banxico. Vuelve a intentar en un minuto.';
            dibujarCetes();
            return;
        }
        const fecha = new Date(datos.fechaSubasta + 'T00:00:00').toLocaleDateString('es-MX');
        const hora = new Date(datos.consultado).toLocaleTimeString('es-MX', {
            timeZone: 'America/Mexico_City', hour: '2-digit', minute: '2-digit', second: '2-digit',
        });
        estadoCetes.textContent = 'Subasta del ' + fecha + ' · Consultado a las ' + hora
            + (datos.consultaFallida ? ' · Banxico no respondió a la consulta más reciente.' : '');
        dibujarCetes();
    } catch (error) {
        estadoCetes.textContent = tasasCetes.length
            ? 'Falló la actualización. Se muestran las últimas tasas consultadas.'
            : error.message;
    } finally {
        botonCetes.disabled = false;
    }
}

// Presenta los plazos y conserva el seleccionado mientras tenga tasa disponible.
function dibujarCetes() {
    if (!tasasCetes.some((tasa) => tasa.dias === plazoCetes && tasa.rendimiento !== null))
        plazoCetes = tasasCetes.find((tasa) => tasa.rendimiento !== null)?.dias;
    listaCetes.innerHTML = tasasCetes.map((tasa) => `<button class="market-row ${tasa.dias === plazoCetes ? 'selected' : ''}" type="button" data-cetes-term="${tasa.dias}" aria-pressed="${tasa.dias === plazoCetes}" ${tasa.rendimiento === null ? 'disabled' : ''}><span><strong>${tasa.dias} días</strong></span><span class="quote"><strong>${tasa.rendimiento === null ? 'Sin tasa publicada' : formatoTasa.format(tasa.rendimiento) + ' %'}</strong></span></button>`).join('');
    const elegida = tasasCetes.find((tasa) => tasa.dias === plazoCetes);
    formularioCetes.querySelector('button').disabled = !elegida;
    elemento('#cetes-selected').textContent = elegida
        ? `CETES a ${elegida.dias} días · ${formatoTasa.format(elegida.rendimiento)} % anual`
        : 'La simulación se habilita cuando Banxico publique una tasa disponible.';
    if (resultadoCetes.childElementCount && elegida) calcularCetes();
    else resultadoCetes.replaceChildren();
}

// Estima el precio descontado y el rendimiento de títulos completos al vencimiento.
function calcularCetes() {
    const elegida = tasasCetes.find((tasa) => tasa.dias === plazoCetes);
    if (!elegida || !formularioCetes.checkValidity()) return;
    const capital = Number(formularioCetes.elements.capital.value);
    const precio = 10 / (1 + elegida.rendimiento / 100 * elegida.dias / 360);
    const titulos = Math.floor(capital / precio);
    if (!titulos) {
        resultadoCetes.textContent = 'El capital es menor al precio estimado de un título.';
        return;
    }
    const rendimiento = titulos * (10 - precio);
    mostrarRendimiento(resultadoCetes, rendimiento, capital + rendimiento);
}

listaCetes?.addEventListener('click', (evento) => {
    const boton = evento.target.closest('[data-cetes-term]');
    if (!boton) return;
    plazoCetes = Number(boton.dataset.cetesTerm);
    dibujarCetes();
    listaCetes.querySelector('[aria-pressed="true"]')?.focus({ preventScroll: true });
});
botonCetes?.addEventListener('click', cargarCetes);
formularioCetes?.addEventListener('input', () => resultadoCetes.replaceChildren());
formularioCetes?.addEventListener('submit', (evento) => {
    evento.preventDefault();
    calcularCetes();
});
setInterval(() => {
    if (!document.hidden && vista === 'mercados' && seccionMercado === 'cetes') cargarCetes();
}, 60000);

// Cotizaciones agrupadas y una gráfica para el activo seleccionado.
const gruposMercado = [
    { tipo: 'Cripto', titulo: 'Cripto', lista: '#market-crypto' },
    { tipo: 'Acción', titulo: 'Acciones', lista: '#market-stocks' },
];
const preciosObservados = new Map();
let periodoMercado = 'historial';
let historialMercado = null;

// Al cambiar de fuente, vuelve a cargar las cotizaciones del modo elegido.
elemento('#market-mode').addEventListener('change', () => {
    modo = elemento('#market-mode').value;
    localStorage.setItem('vantex-modo', modo);
    operacionPendiente = null;
    cotizaciones = [];
    historialMercado = null;
    gruposMercado.forEach((grupo) => (elemento(grupo.lista).textContent = ''));
    elemento('#chart-price').textContent = '—';
    elemento('#market-chart').textContent = 'Consultando cotizaciones…';
    elemento('#chart-source').textContent = '';
    elemento('#quote-note').textContent = '';
    actualizarEstimacion();
    cargarMercado();
});
elemento('#refresh-market').addEventListener('click', cargarMercado);
elemento('#market-list').addEventListener('click', (evento) => {
    const fila = evento.target.closest('button[data-symbol]');
    if (fila) seleccionarActivo(fila.dataset.symbol);
});
elemento('#trade-symbol').addEventListener('change', (evento) =>
    seleccionarActivo(evento.target.value),
);
elemento('#market-chart-periods')?.addEventListener('click', (evento) => {
    const boton = evento.target.closest('button[data-period]');
    if (!boton) return;
    periodoMercado = boton.dataset.period;
    dibujarGraficaMercado();
});

// Agrupa activos, actualiza sus precios y conserva las muestras recibidas en la sesión.
async function cargarMercado() {
    if (cargandoMercado) return;
    cargandoMercado = true;
    elemento('#refresh-market').disabled = true;
    const solicitado = modo;
    elemento('#market-note').textContent =
        modo === 'demo'
            ? 'Demostración: precios y gráficas ficticios para practicar.'
            : estado?.acciones
              ? 'Coinbase actualiza Cripto. En Acciones, la frecuencia depende del plan de Alpha Vantage.'
              : 'Coinbase actualiza Cripto. Para consultar precios de Acciones se requiere una clave de Alpha Vantage. Puedes practicar con Acciones en Demostración.';
    try {
        const datos = await api('/mercado?modo=' + solicitado);
        if (solicitado !== modo) return;
        cotizaciones = datos;
        const foco = elemento('#market-list').contains(document.activeElement)
            ? document.activeElement.dataset.symbol
            : null;
        const opciones = [];
        for (const grupo of gruposMercado) {
            const activos = datos.filter((c) => c.tipo === grupo.tipo);
            elemento(grupo.lista).innerHTML = activos
                .map((c) => `<button type="button" class="market-row" data-symbol="${escaparHtml(c.simbolo)}"><span><strong>${escaparHtml(c.nombre)}</strong><small>${escaparHtml(c.simbolo)}</small></span><span class="quote"><strong${c.precio === null ? ' class="quote-unavailable"' : ''}>${c.precio === null ? 'Sin cotización' : formatoMoneda(c.precio)}</strong>${c.precio === null ? '' : `<small>${escaparHtml(c.fuente)}</small>`}</span></button>`)
                .join('');
            opciones.push(`<optgroup label="${grupo.titulo}">${activos
                .map((c) => `<option value="${escaparHtml(c.simbolo)}">${escaparHtml(c.nombre)} (${escaparHtml(c.simbolo)})</option>`)
                .join('')}</optgroup>`);
        }
        const selector = elemento('#trade-symbol');
        if (selector.options.length !== datos.length)
            selector.innerHTML = opciones.join('');
        for (const c of datos) {
            if (!c.precio || !c.consultado || (c.modo !== 'demo' && !c.tiempoReal)) continue;
            const clave = solicitado + '/' + c.simbolo;
            const puntos = preciosObservados.get(clave) || [];
            const x = new Date(c.consultado).getTime();
            if (puntos.length && x <= puntos.at(-1).x) continue;
            puntos.push({ x, y: c.precio });
            preciosObservados.set(clave, puntos.slice(-90));
        }
        void seleccionarActivo(simbolo);
        if (foco) {
            Array.from(elemento('#market-list').querySelectorAll('button'))
                .find((fila) => fila.dataset.symbol === foco)
                ?.focus({ preventScroll: true });
        }
    } catch (e) {
        aviso(e.message, true);
    } finally {
        cargandoMercado = false;
        elemento('#refresh-market').disabled = false;
        if (solicitado !== modo) cargarMercado();
    }
}

// Cambia el activo de la gráfica y sincroniza el selector del simulador.
async function seleccionarActivo(nuevo) {
    simbolo = nuevo;
    elemento('#trade-symbol').value = nuevo;
    elemento('#market-list').querySelectorAll('[data-symbol]').forEach((fila) => {
        const seleccionado = fila.dataset.symbol === nuevo;
        fila.classList.toggle('selected', seleccionado);
        fila.setAttribute('aria-pressed', seleccionado);
    });
    const c = cotizaciones.find((c) => c.simbolo === nuevo);
    if (!c) return;
    actualizarEstimacion();
    elemento('#chart-symbol').textContent = c.simbolo;
    elemento('#chart-title').textContent = c.nombre;
    elemento('#chart-price').textContent = formatoMoneda(c.precio);
    elemento('#quote-note').textContent =
        c.nota + (c.consultado ? ' Consulta del servidor: ' + new Date(c.consultado).toLocaleString('es-MX') + '.' : '');

    const clave = modo + '/' + nuevo;
    if (historialMercado?.clave !== clave)
        historialMercado = { clave, datos: null, consultado: 0, esperando: false };
    const historial = historialMercado;
    dibujarGraficaMercado();
    if (historial.esperando || Date.now() - historial.consultado < 60000) return;
    historial.esperando = true;
    try {
        historial.datos = await api('/historial/' + nuevo + '?modo=' + modo);
    } catch {
        // Conserva el último historial que sí se pudo consultar.
    } finally {
        historial.consultado = Date.now();
        historial.esperando = false;
        if (historialMercado === historial) dibujarGraficaMercado();
    }
}

// Alterna entre cierres históricos y cotizaciones observadas durante la sesión.
function dibujarGraficaMercado() {
    if (!historialMercado) return;
    const historial = historialMercado.datos;
    const observados = preciosObservados.get(historialMercado.clave) || [];
    if (!observados.length) periodoMercado = 'historial';
    elemento('#market-chart-periods').querySelectorAll('button').forEach((boton) => {
        boton.hidden = boton.dataset.period === 'seguimiento' && !observados.length;
        boton.setAttribute('aria-pressed', boton.dataset.period === periodoMercado);
    });
    const grafico = elemento('#market-chart');
    const fuente = elemento('#chart-source');
    const etiqueta = (x) => new Date(x).toLocaleString('es-MX', {
        month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
    });
    if (periodoMercado === 'seguimiento') {
        fuente.textContent = 'Cotizaciones recibidas durante esta sesión.';
        if (observados.length < 2) {
            grafico.textContent = 'Esperando la siguiente cotización para trazar la gráfica.';
            return;
        }
        grafica(grafico, observados, (x) => new Date(x).toLocaleTimeString('es-MX'));
        return;
    }
    if (!historial) {
        grafico.textContent = historialMercado.consultado ? 'Historial no disponible.' : 'Consultando historial…';
        fuente.textContent = '';
        return;
    }
    const puntos = historial.puntos.map((p) => ({ x: new Date(p.fecha).getTime(), y: p.precio }));
    const ultima = observados.at(-1);
    if (ultima && ultima.x > (puntos.at(-1)?.x ?? 0)) puntos.push(ultima);
    grafica(grafico, puntos, etiqueta);
    fuente.textContent = historial.fuente + (ultima ? ' · Incluye la última cotización recibida.' : '');
}
// Multiplica unidades por precio para mostrar un importe aproximado de la orden.
function actualizarEstimacion() {
    const formulario = elemento('#trade-form');
    const simboloElegido = formulario.elements.simbolo.value;
    const cantidad = Number(formulario.elements.cantidad.value);
    let precio = null;
    for (const cotizacion of cotizaciones) {
        if (cotizacion.simbolo === simboloElegido) {
            precio = cotizacion.precio;
        }
    }
    const resultado = elemento('#trade-estimate');
    if (precio === null || cantidad <= 0) {
        resultado.textContent =
            'Selecciona un activo con cotización y una cantidad positiva.';
        return;
    }
    resultado.textContent =
        'Importe aproximado: ' +
        formatoMoneda(precio * cantidad) +
        '. El servidor confirma el precio al registrar la operación.';
}

elemento('#trade-form').addEventListener('input', actualizarEstimacion);
elemento('#trade-form').addEventListener('change', actualizarEstimacion);

// Los alias de depuración de Android pueden usar HTTP. El UUID sigue siendo criptográfico.
function crearIdOperacion() {
    if (crypto.randomUUID) return crypto.randomUUID();
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const id = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
    return [id.slice(0, 8), id.slice(8, 12), id.slice(12, 16), id.slice(16, 20), id.slice(20)].join('-');
}

// Valida la orden, envíala al servidor y actualiza el saldo y el historial al recibir confirmación.
elemento('#trade-form').addEventListener('submit', async (evento) => {
    evento.preventDefault();
    if (!usuario) {
        abrirAcceso();
        return;
    }
    const boton = evento.currentTarget.querySelector('button');
    boton.disabled = true;
    const campos = leerFormulario(evento.currentTarget);
    const orden = {
        simbolo: campos.simbolo,
        tipo: campos.tipo,
        cantidad: Number(campos.cantidad),
        modo,
    };
    const firma = JSON.stringify(orden);
    // Conserva el identificador al reintentar la misma orden si se perdió la respuesta.
    if (!operacionPendiente || operacionPendiente.firma !== firma)
        operacionPendiente = { firma, id: crearIdOperacion() };
    try {
        const respuesta = await api('/operaciones', {
            ...orden,
            solicitudId: operacionPendiente.id,
        });
        operacionPendiente = null;
        elemento('#trade-result').textContent =
            respuesta.mensaje +
            (respuesta.total
                ? ' Total: ' + formatoMoneda(respuesta.total) + '.'
                : '');
        await cargarCuenta();
        aviso(respuesta.mensaje);
    } catch (e) {
        elemento('#trade-result').textContent = e.message;
    } finally {
        boton.disabled = false;
    }
});

// Gráficas SVG sin biblioteca externa. La práctica usa una escala fija para comparar.
// Escala los datos al área SVG y traza ejes, guías y serie para las vistas que la comparten.
function grafica(contenedor, puntos, etiquetaX, escala) {
    if (puntos.length < (escala ? 1 : 2)) {
        contenedor.textContent =
            'Aún no hay suficientes puntos para dibujar una gráfica.';
        return;
    }
    const w = 600,
        h = 220,
        left = 70,
        right = 18,
        top = 18,
        bottom = 40;
    const ys = puntos.map((p) => Number(p.y)),
        xs = puntos.map((p) => Number(p.x));
    const min = Math.min(...ys),
        max = Math.max(...ys),
        margen = (max - min) * 0.15 || Math.max(max * 0.02, 1);
    const bajo = escala?.minimo ?? min - margen,
        alto = escala?.maximo ?? max + margen,
        fin = escala?.fin ?? xs.at(-1);
    const X = (x) =>
        left + ((x - xs[0]) / (fin - xs[0] || 1)) * (w - left - right);
    const Y = (y) => top + ((alto - y) / (alto - bajo)) * (h - top - bottom);
    const linea = puntos
        .map(
            (p, i) =>
                (i ? 'L' : 'M') + X(p.x).toFixed(2) + ',' + Y(p.y).toFixed(2),
        )
        .join(' ');
    const guias = [0, 0.5, 1]
        .map((f) => {
            const y = top + f * (h - top - bottom);
            const valor = alto - f * (alto - bajo);
            const referencia = escala && f === 0.5 ? 'stroke-dasharray="5 4"' : '';
            const color = referencia ? '#6e8199' : '#2b394d';
            return `<line x1="${left}" y1="${y}" x2="${w - right}" y2="${y}" stroke="${color}" ${referencia}/><text x="${left - 8}" y="${y + 4}" text-anchor="end" fill="#9aaec5" font-size="10">${escaparHtml(formatoNumero(Math.round(valor * 100) / 100))}</text>`;
        })
        .join('');
    contenedor.innerHTML = `<svg viewBox="0 0 ${w} ${h}" role="img" aria-label="Gráfica de ${escaparHtml(etiquetaX(xs[0]))} a ${escaparHtml(etiquetaX(xs.at(-1)))}"><title>Inicio ${escaparHtml(formatoNumero(ys[0]))}; último valor ${escaparHtml(formatoNumero(ys.at(-1)))}</title>${guias}<path d="${linea} L${X(xs.at(-1))},${h - bottom} L${left},${h - bottom} Z" fill="#74dfac" opacity="0.07"/><path d="${linea}" fill="none" stroke="#88efbd" stroke-width="2.5"/><circle cx="${X(xs.at(-1))}" cy="${Y(ys.at(-1))}" r="4" fill="#88efbd"/><text x="${left}" y="${h - 10}" fill="#9aaec5" font-size="10">${escaparHtml(etiquetaX(xs[0]))}</text><text x="${w - right}" y="${h - 10}" text-anchor="end" fill="#9aaec5" font-size="10">${escaparHtml(etiquetaX(fin))}</text></svg>`;
}

// Saldos ficticios para que puedas repetir y comparar la misma situación.
const inversionesPractica = [
    {
        nombre: 'Renta fija a un mes',
        detalle: 'Riesgo bajo · Disponible al vencimiento',
        saldos: [1000, 1001, 1001, 1002, 1003, 1004, 1004, 1005, 1006, 1007, 1007, 1008, 1009],
    },
    {
        nombre: 'Fondo de acciones diversificado',
        detalle: 'Riesgo medio · Repartido entre muchas empresas',
        saldos: [1000, 1009, 997, 1014, 1004, 980, 995, 978, 993, 986, 1003, 988, 995],
    },
    {
        nombre: 'Una criptomoneda',
        detalle: 'Riesgo alto · Concentrado en un activo',
        saldos: [1000, 1060, 960, 1090, 910, 1040, 880, 1120, 1005, 1150, 935, 1090, 1080],
    },
];

const casosPractica = {
    cripto: {
        situacion: 'En una prueba con dinero virtual quieres identificar cuál de estas inversiones puede cambiar de valor más bruscamente.',
        adecuada: 2,
        explicacion: 'Una criptomoneda puede tener subidas y caídas fuertes en un mismo periodo. Mira las caídas durante el recorrido además del saldo final.',
    },
    riesgo: {
        situacion: 'Tienes una meta a cinco años. Aceptas que el saldo pueda bajar y quieres repartir el riesgo entre muchas empresas.',
        adecuada: 1,
        explicacion: 'El fondo distribuye el dinero entre varias empresas. Eso reduce la dependencia de una sola, aunque el fondo todavía puede perder valor.',
    },
    metas: {
        situacion: 'Necesitas el dinero en un mes para un gasto planeado. Quieres que esté disponible en esa fecha y priorizas que su valor se mantenga estable.',
        adecuada: 0,
        explicacion: 'En este caso, la renta fija vence justo cuando necesitas el dinero y su saldo varía poco. Una inversión más volátil puede caer cuando te toca pagar.',
    },
};

// Construye el mini ejercicio y sus opciones para la lección indicada.
function practicaInversion(id) {
    const caso = casosPractica[id];
    if (!caso) return '';
    return `
        <section class="lesson-practice">
            <h3>Práctica de inversión</h3>
            <p>${caso.situacion}</p>
            <form data-practica="${id}">
                <fieldset>
                    <legend>¿Qué opción encaja con la situación?</legend>
                    <div class="practice-options">
                        ${inversionesPractica.map((inversion, i) => `
                            <label>
                                <input type="radio" name="inversion" value="${i}" required>
                                <span><strong>${inversion.nombre}</strong><small>${inversion.detalle}</small></span>
                            </label>
                        `).join('')}
                    </div>
                    <button class="button" type="submit">Ver simulación</button>
                </fieldset>
            </form>
            <p class="fine">Parte de $1,000 MXN virtuales. Puedes probar otra opción con la misma escala.</p>
            <div class="practice-result" hidden>
                <h4 data-inversion></h4>
                <div class="practice-summary">
                    <div><small>Saldo simulado (MXN)</small><strong data-saldo></strong></div>
                    <span data-cambio></span>
                    <span data-paso></span>
                </div>
                <div class="chart"></div>
                <p class="fine">La línea punteada marca el saldo inicial. Los valores son ficticios.</p>
                <p class="practice-feedback" role="status"></p>
            </div>
        </section>`;
}

// Reproduce la serie del escenario elegido y muestra retroalimentación con la misma escala.
function simularPractica(evento) {
    evento.preventDefault();
    const formulario = evento.currentTarget;
    const eleccion = Number(leerFormulario(formulario).inversion);
    const caso = casosPractica[formulario.dataset.practica];
    const inversion = inversionesPractica[eleccion];
    const practica = formulario.closest('.lesson-practice');
    const resultado = practica.querySelector('.practice-result');
    const grafico = resultado.querySelector('.chart');
    const saldo = resultado.querySelector('[data-saldo]');
    const cambio = resultado.querySelector('[data-cambio]');
    const progreso = resultado.querySelector('[data-paso]');
    const mensaje = resultado.querySelector('.practice-feedback');
    const opciones = formulario.querySelector('fieldset');
    const boton = formulario.querySelector('button');
    const inicial = inversion.saldos[0];
    const ultimoPaso = inversion.saldos.length - 1;
    const escala = { minimo: 800, maximo: 1200, fin: ultimoPaso };
    const puntos = [];
    let paso = 0;

    function mostrarPaso() {
        const valor = inversion.saldos[paso];
        puntos.push({ x: paso, y: valor });
        const variacion = ((valor - inicial) / inicial) * 100;
        const signo = variacion > 0 ? '+' : '';
        saldo.textContent = formatoMXN(valor);
        cambio.textContent = signo + variacion.toFixed(1) + ' % desde el inicio';
        cambio.classList.toggle('positive', variacion > 0);
        cambio.classList.toggle('negative', variacion < 0);
        progreso.textContent = `Paso ${paso} de ${ultimoPaso}`;
        grafica(grafico, puntos, (x) => 'Paso ' + x, escala);
    }

    resultado.querySelector('[data-inversion]').textContent = inversion.nombre;
    resultado.hidden = false;
    opciones.disabled = true;
    boton.textContent = 'Simulando…';
    mensaje.textContent = 'Simulación en curso.';
    mostrarPaso();
    resultado.scrollIntoView({ block: 'nearest' });

    const reloj = setInterval(() => {
        paso++;
        mostrarPaso();
        if (paso === ultimoPaso) {
            clearInterval(reloj);
            opciones.disabled = false;
            boton.textContent = 'Simular de nuevo';
            const comentario = eleccion === caso.adecuada
                ? 'Tu elección encaja con el caso. '
                : `La opción que mejor encaja es «${inversionesPractica[caso.adecuada].nombre}». `;
            mensaje.textContent = comentario + caso.explicacion;
        }
    }, 650);
}

// Presenta las lecciones y conecta cuestionarios y prácticas sin borrar el estado ya visible.
function dibujarLecciones() {
    const hechas = cuenta?.lecciones || [];
    elemento('#progress-label').textContent =
        hechas.length + ' de ' + lecciones.length + ' completadas';
    elemento('#progress-bar').style.width =
        (lecciones.length ? (hechas.length / lecciones.length) * 100 : 0) + '%';
    // Las lecciones se cargan una vez. Actualizar el progreso conserva respuestas y simulaciones.
    const lista = elemento('#lessons');
    if (lista.childElementCount) {
        lista.querySelectorAll('[data-lesson]').forEach((leccion, i) => {
            leccion.querySelector('.lesson-mark').textContent = hechas.includes(leccion.dataset.lesson)
                ? '✓ '
                : String(i + 1).padStart(2, '0') + ' · ';
        });
        return;
    }
    lista.innerHTML = lecciones.map((l, i) => `
        <details class="lesson" data-lesson="${escaparHtml(l.id)}">
            <summary>
                <span class="lesson-mark">${hechas.includes(l.id) ? '✓ ' : String(i + 1).padStart(2, '0') + ' · '}</span>${escaparHtml(l.titulo)}
                <small>${escaparHtml(l.nivel)} · ${escaparHtml(l.duracion)}${casosPractica[l.id] ? ' · Incluye práctica' : ''}</small>
            </summary>
            <p>${escaparHtml(l.texto)}</p>
            <p class="example">${escaparHtml(l.ejemplo)}</p>
            <form class="quiz" data-quiz="${escaparHtml(l.id)}">
                <h3>${escaparHtml(l.pregunta)}</h3>
                ${l.opciones.map((o, n) => `<label><input type="radio" name="respuesta" value="${n}" required>${escaparHtml(o)}</label>`).join('')}
                <button class="button secondary" type="submit">Comprobar lo aprendido</button>
                <p role="status"></p>
            </form>
            ${practicaInversion(l.id)}
        </details>`).join('');
    lista.querySelectorAll('[data-practica]').forEach((formulario) =>
        formulario.addEventListener('submit', simularPractica),
    );
    document.querySelectorAll('[data-quiz]').forEach((f) =>
        f.addEventListener('submit', async (evento) => {
            evento.preventDefault();
            if (!usuario) {
                abrirAcceso();
                return;
            }
            const boton = f.querySelector('button');
            boton.disabled = true;
            try {
                const r = await api(
                    '/lecciones/' + f.dataset.quiz + '/responder',
                    { respuesta: Number(new FormData(f).get('respuesta')) },
                );
                f.querySelector('[role=status]').textContent =
                    (r.correcta ? 'Correcto. ' : 'Inténtalo de nuevo. ') +
                    r.explicacion;
                if (r.correcta) {
                    cuenta = await api('/cuenta');
                    dibujarLecciones();
                }
            } catch (e) {
                f.querySelector('[role=status]').textContent = e.message;
            } finally {
                boton.disabled = false;
            }
        }),
    );
}
// Laboratorio de inversiones de la Academia: proyecciones hipotéticas en MXN.
const formatoMXN = (valor) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN', maximumFractionDigits: 2 }).format(valor);
// Acumula aportaciones de fin de mes usando el rendimiento compuesto de cada escenario.
function valorFuturoInversion(capital, mensual, tasaAnual, meses) {
    const tasaMensual = Math.pow(1 + tasaAnual / 100, 1 / 12) - 1;
    let saldo = capital;
    for (let mes = 0; mes < meses; mes++) {
        saldo = saldo * (1 + tasaMensual) + mensual;
    }
    return saldo;
}
// Compara escenarios educativos de pérdida, referencia y ganancia.
function calcularSimuladorAcademia() {
    const form = elemento('#academy-investment-form');
    if (!form) return;
    const d = leerFormulario(form);
    const ingreso = Math.max(0, Number(d.ingreso) || 0);
    const ahorro = Math.max(0, Number(d.ahorro) || 0);
    const capital = Math.max(0, Number(d.capital) || 0);
    const mensual = Math.max(0, Number(d.mensual) || 0);
    const anios = Math.max(1, Number(d.anios) || 1);
    const meses = anios * 12;
    const aportado = capital + mensual * meses;
    const negativo = Number(d.negativo);
    const moderado = Number(d.moderado);
    const positivo = Number(d.positivo);
    const advertencias = [];
    if (capital > ahorro) advertencias.push('El capital inicial supera tu ahorro acumulado; revisa que no estés comprometiendo dinero que necesitas.');
    if (mensual > ingreso) advertencias.push('La aportación mensual supera tu ingreso mensual declarado.');
    if (positivo < moderado || moderado < negativo) advertencias.push('Ordena las tasas: el escenario negativo debe ser el menor y el positivo el mayor.');
    elemento('#academy-investment-warning').textContent = advertencias.length
        ? advertencias.join(' ')
        : `Simulación para ${escaparHtml(d.tipoActivo)}. Tus aportaciones suman ${formatoMXN(aportado)} durante ${anios} año(s).`;
    elemento('#academy-investment-warning').classList.toggle('negative', advertencias.length > 0);
    elemento('#academy-total-contributed').textContent = formatoMXN(aportado);
    elemento('#academy-period').textContent = anios + (anios === 1 ? ' año' : ' años');
    const escenarios = [
        { id: 'loss', tasa: negativo, valor: valorFuturoInversion(capital, mensual, negativo, meses) },
        { id: 'neutral', tasa: moderado, valor: valorFuturoInversion(capital, mensual, moderado, meses) },
        { id: 'gain', tasa: positivo, valor: valorFuturoInversion(capital, mensual, positivo, meses) },
    ];
    for (const escenario of escenarios) {
        const diferencia = escenario.valor - aportado;
        elemento(`#academy-${escenario.id}-value`).textContent = formatoMXN(escenario.valor);
        const detalle = diferencia >= 0
            ? `Ganancia estimada: +${formatoMXN(diferencia)}`
            : `Pérdida estimada: −${formatoMXN(Math.abs(diferencia))}`;
        elemento(`#academy-${escenario.id}-detail`).textContent = `${detalle} · ${escenario.tasa}% anual`;
    }
    const peor = escenarios[0].valor - aportado;
    const mejor = escenarios[2].valor - aportado;
    let explicacion = `Con ${formatoMXN(aportado)} de aportaciones, el escenario negativo termina en ${formatoMXN(escenarios[0].valor)} y el positivo en ${formatoMXN(escenarios[2].valor)}. `;
    explicacion += peor < 0
        ? `En el escenario negativo perderías ${formatoMXN(Math.abs(peor))} frente a lo que aportaste. `
        : 'Con esta tasa negativa hipotética, el valor final aún supera tus aportaciones; eso no elimina el riesgo. ';
    explicacion += mejor >= 0
        ? `En el positivo la diferencia sería de ${formatoMXN(mejor)}. Recuerda que el rendimiento real puede ser distinto.`
        : 'Incluso el escenario positivo seleccionado podría terminar por debajo de tus aportaciones.';
    elemento('#academy-investment-explanation').textContent = explicacion;
}
const academyInvestmentForm = elemento('#academy-investment-form');
academyInvestmentForm.addEventListener('input', calcularSimuladorAcademia);
academyInvestmentForm.addEventListener('change', calcularSimuladorAcademia);
academyInvestmentForm.addEventListener('submit', (evento) => {
    evento.preventDefault();
    calcularSimuladorAcademia();
});
calcularSimuladorAcademia();

// Lee los montos y la tasa anual solicitados para la calculadora de metas.
function datosMeta() {
    const d = leerFormulario(elemento('#goal-form'));
    return {
        nombre: d.nombre,
        costo: Number(d.costo),
        inicial: Number(d.inicial),
        mensual: Number(d.mensual),
        tasaAnual: Number(d.tasaAnual),
    };
}
// Solicita una proyección y guarda la meta cuando la acción viene del botón Guardar.
async function proyectar(guardar = false) {
    if (!elemento('#goal-form').reportValidity()) return;
    if (guardar && !usuario) {
        abrirAcceso();
        return;
    }
    const botones = elemento('#goal-form').querySelectorAll('button');
    botones.forEach((b) => (b.disabled = true));
    try {
        const r = await api(
            guardar ? '/metas' : '/metas/proyectar',
            datosMeta(),
        );
        elemento('#goal-headline').textContent =
            r.meses === null
                ? 'No se alcanza en 600 meses.'
                : r.meses === 0
                  ? 'Tu ahorro ya cubre la meta.'
                  : r.meses + ' meses en este escenario.';
        elemento('#goal-result').textContent =
            'Sin rendimiento: ' +
            formatoNumero(r.sinRendimiento) +
            ' meses. ' +
            r.nota;
        elemento('#goal-advice').textContent = r.consejo;
        grafica(
            elemento('#goal-chart'),
            r.puntos.map((p) => ({ x: p.mes, y: p.saldo })),
            (x) => 'Mes ' + x,
        );
        if (guardar) {
            await cargarCuenta();
            aviso('Meta guardada.');
        }
    } catch (e) {
        elemento('#goal-result').textContent = e.message;
    } finally {
        botones.forEach((b) => (b.disabled = false));
    }
}
elemento('#goal-form').addEventListener('submit', (e) => {
    e.preventDefault();
    proyectar();
});
elemento('#save-goal').addEventListener('click', () => proyectar(true));
// Agrega un mensaje al chat como texto y ajusta la posición de desplazamiento.
function burbuja(texto, tipo) {
    const div = document.createElement('div');
    div.className = 'message ' + tipo;
    div.textContent = texto;
    elemento('#messages').append(div);
    elemento('#messages').scrollTop = elemento('#messages').scrollHeight;
    return div;
}
const historialTutor = [];
const bienvenidaTutor = elemento('#messages').firstElementChild.cloneNode(true);
const nuevoChat = elemento('#new-chat');
const modosTutor = { ia: 'Tutor con IA', local: 'IA local', biblioteca: 'Biblioteca educativa' };

let precargaTutor = 0;

// Pide precargar el modelo local al entrar con una sesión activa.
function prepararTutor() {
    if (!usuario || estado?.tutor !== 'local' || !estado.tutorEnFlujo
        || Date.now() - precargaTutor < 120000) return;
    precargaTutor = Date.now();
    api('/chat/preparar', {}).then((r) => {
        if (!r.preparado) precargaTutor = 0;
    }).catch(() => { precargaTutor = 0; });
}

// Lee el formato NDJSON por partes, completa las líneas partidas y dibuja el texto progresivo.
async function leerRespuestaTutor(respuesta, mostrar) {
    const lector = respuesta.body.pipeThrough(new TextDecoderStream()).getReader();
    let pendiente = '';
    let resultado;
    try {
        while (true) {
            const { value, done } = await lector.read();
            if (done) break;
            const lineas = (pendiente + value).split('\n');
            pendiente = lineas.pop();
            for (const linea of lineas) {
                if (!linea) continue;
                const evento = JSON.parse(linea);
                if (evento.tramo) mostrar(evento.tramo);
                if (evento.resultado) resultado = evento.resultado;
            }
        }
    } finally {
        lector.releaseLock();
    }
    if (!resultado) throw new Error('Se interrumpió la respuesta del tutor. Intenta de nuevo.');
    return resultado;
}

// Restablece la conversación del navegador y vuelve a mostrar el estado del tutor.
function reiniciarTutor() {
    historialTutor.length = 0;
    elemento('#messages').replaceChildren(bienvenidaTutor.cloneNode(true));
    elemento('#question').value = '';
    elemento('#ai-status').textContent = modosTutor[estado?.tutor] || modosTutor.biblioteca;
    elemento('#chat-note').textContent = estado?.tutor === 'local'
        ? 'El modelo se prepara al abrir el tutor y muestra la respuesta conforme la escribe.'
        : '';
}

nuevoChat?.addEventListener('click', () => {
    reiniciarTutor();
    elemento('#question').focus();
});
// Envía el mensaje con el contexto de la conversación y actualiza la respuesta conforme llegan las partes.
elemento('#chat-form').addEventListener('submit', async (evento) => {
    evento.preventDefault();
    if (!usuario) {
        abrirAcceso();
        return;
    }
    const entrada = elemento('#question');
    const mensaje = entrada.value.trim();
    const boton = evento.currentTarget.querySelector('button');
    if (!mensaje || boton.disabled) return;
    boton.disabled = true;
    if (nuevoChat) nuevoChat.disabled = true;
    burbuja(mensaje, 'user');
    entrada.value = '';
    const espera = burbuja('Preparando una explicación…', 'assistant');
    try {
        const datos = { mensaje, historial: historialTutor };
        let texto = '';
        const r = estado?.tutorEnFlujo && estado.tutor === 'local'
            ? await api('/chat/stream', datos, (respuesta) => leerRespuestaTutor(respuesta, (tramo) => {
                if (!espera.isConnected) return;
                texto += tramo;
                espera.textContent = texto;
                elemento('#messages').scrollTop = elemento('#messages').scrollHeight;
            }))
            : await api('/chat', datos);
        if (!espera.isConnected) return;
        espera.textContent = r.respuesta;
        elemento('#ai-status').textContent = modosTutor[r.modo];
        elemento('#chat-note').textContent = r.nota;
        if (r.modo !== 'biblioteca') {
            historialTutor.push(
                { rol: 'user', texto: mensaje.slice(0, 600) },
                { rol: 'assistant', texto: r.respuesta.slice(0, 600) },
            );
            if (historialTutor.length > 4) historialTutor.splice(0, 2);
        }
    } catch (e) {
        if (espera.isConnected) {
            espera.textContent = e.message;
            if (!entrada.value) entrada.value = mensaje;
        }
    } finally {
        boton.disabled = false;
        if (nuevoChat) nuevoChat.disabled = false;
        elemento('#messages').scrollTop = elemento('#messages').scrollHeight;
    }
});
document.querySelectorAll('[data-question]').forEach((b) =>
    b.addEventListener('click', () => {
        elemento('#question').value = b.dataset.question;
        elemento('#question').focus();
    }),
);

// Finanzas personales: simulaciones educativas en MXN, separadas del saldo virtual en USD.
const financeKey = () => 'vantex-finanzas-' + (usuario?.id || 'invitado');
const moneyMXN = (value) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(Number(value) || 0);
// Recupera el presupuesto y las metas que se guardaron para este usuario en el navegador.
function cargarFinanzas() {
    const guardado = localStorage.getItem(financeKey());
    if (guardado) {
        try {
            const datos = JSON.parse(guardado);
            for (const selector of ['#finance-form', '#scenario-form', '#finance-goal-form']) {
                const form = elemento(selector);
                for (const [clave, valor] of Object.entries(datos)) {
                    if (form.elements[clave]) form.elements[clave].value = valor;
                }
            }
            elemento('#finance-save-status').textContent = 'Datos recuperados de este navegador.';
        } catch { localStorage.removeItem(financeKey()); }
    }
    actualizarResumenFinanzas();
    calcularEscenarios();
    calcularMetaFinanciera();
}
// Convierte montos del presupuesto a números para los cálculos del módulo personal.
function valoresFinanzas() {
    const d = leerFormulario(elemento('#finance-form'));
    return Object.fromEntries(Object.entries(d).map(([k, v]) => [k, Number(v) || 0]));
}
// Calcula dinero disponible y presupuesto de inversión a partir de ingresos y gastos.
function actualizarResumenFinanzas() {
    const d = valoresFinanzas();
    const disponible = d.ingreso - d.gastos;
    elemento('#finance-available').textContent = moneyMXN(disponible);
    elemento('#finance-saved').textContent = moneyMXN(d.ahorro);
    elemento('#finance-investment').textContent = moneyMXN(d.inversion);
    elemento('#finance-guidance').textContent = disponible < 0
        ? 'Tus gastos superan tus ingresos. Antes de invertir, revisa tu presupuesto y prioriza cubrir tus necesidades.'
        : d.inversion > disponible
          ? 'Tu presupuesto de inversión supera el dinero que te queda después de gastos. Ajusta la cantidad para evitar comprometer dinero que necesitas.'
          : 'Tu presupuesto de inversión está dentro del dinero disponible declarado. Recuerda conservar liquidez y un fondo para imprevistos.';
}
// Aplica un porcentaje elegido a un capital para mostrar una subida y una caída hipotéticas.
function calcularEscenarios() {
    const f = elemento('#scenario-form');
    if (!f) return;
    const d = leerFormulario(f);
    const capital = Math.max(0, Number(d.capital) || 0);
    const positivo = Math.max(0, Number(d.positivo) || 0) / 100;
    const negativo = Math.min(100, Math.max(0, Number(d.negativo) || 0)) / 100;
    const ganancia = capital * positivo;
    const perdida = capital * negativo;
    elemento('#scenario-results').innerHTML = `
      <div><span>Si sube ${ (positivo * 100).toLocaleString('es-MX') }%</span><strong class="positive">${moneyMXN(capital + ganancia)}</strong><small>Ganancia: +${moneyMXN(ganancia)}</small></div>
      <div><span>Si baja ${ (negativo * 100).toLocaleString('es-MX') }%</span><strong class="negative">${moneyMXN(capital - perdida)}</strong><small>Pérdida: −${moneyMXN(perdida)}</small></div>`;
}
// Calcula cuánto falta para la meta y estima los meses necesarios a partir de las aportaciones.
function calcularMetaFinanciera() {
    const f = elemento('#finance-goal-form');
    if (!f) return;
    const d = leerFormulario(f);
    const objetivo = Number(d.objetivo) || 0;
    const ahorrado = Number(d.metaAhorrado) || 0;
    const mensual = Number(d.metaMensual) || 0;
    const falta = Math.max(0, objetivo - ahorrado);
    const meses = mensual > 0 ? Math.ceil(falta / mensual) : null;
    const fecha = meses === null ? '' : new Date(new Date().getFullYear(), new Date().getMonth() + meses, 1).toLocaleDateString('es-MX', { month: 'long', year: 'numeric' });
    const avance = objetivo > 0 ? Math.min(100, ahorrado / objetivo * 100) : 0;
    elemento('#finance-goal-result').innerHTML = `<strong>${escaparHtml(d.nombreMeta || 'Tu meta')}</strong><p>Te faltan <b>${moneyMXN(falta)}</b> para alcanzar tu objetivo.</p><div class="progress-track"><div style="width:${avance.toFixed(1)}%"></div></div><p>Avance: ${avance.toFixed(1)}% · ${meses === null ? 'Ingresa una aportación mensual mayor a cero.' : meses === 0 ? '¡Ya alcanzaste tu meta!' : `Tiempo estimado: ${meses} meses (aprox. ${fecha}).`}</p>`;
}
// Los controles recalculan el resumen al escribir y guardan los datos al enviar el formulario.
elemento('#finance-form').addEventListener('input', actualizarResumenFinanzas);
elemento('#finance-form').addEventListener('submit', (e) => {
    e.preventDefault();
    const d = { ...valoresFinanzas(), ...leerFormulario(elemento('#scenario-form')), ...leerFormulario(elemento('#finance-goal-form')) };
    if (d.gastos > d.ingreso) aviso('Tus gastos superan tus ingresos. Revisa tu presupuesto antes de asignar dinero a inversiones.', true);
    if (d.inversion > Math.max(0, d.ingreso - d.gastos)) aviso('El presupuesto para invertir supera tu dinero disponible después de gastos.', true);
    localStorage.setItem(financeKey(), JSON.stringify(d));
    elemento('#finance-save-status').textContent = 'Situación financiera guardada en este navegador.';
    aviso('Tus datos financieros se guardaron en este navegador.');
});
elemento('#scenario-form').addEventListener('input', calcularEscenarios);
elemento('#scenario-form').addEventListener('submit', (e) => { e.preventDefault(); calcularEscenarios(); const datos = { ...valoresFinanzas(), ...leerFormulario(elemento('#scenario-form')), ...leerFormulario(elemento('#finance-goal-form')) }; localStorage.setItem(financeKey(), JSON.stringify(datos)); });
elemento('#finance-goal-form').addEventListener('input', calcularMetaFinanciera);
elemento('#finance-goal-form').addEventListener('submit', (e) => { e.preventDefault(); calcularMetaFinanciera(); const datos = { ...valoresFinanzas(), ...leerFormulario(elemento('#scenario-form')), ...leerFormulario(elemento('#finance-goal-form')) }; localStorage.setItem(financeKey(), JSON.stringify(datos)); });

// Carga estado, sesión y lecciones antes de abrir la vista inicial solicitada.
async function iniciar() {
    dibujarCuenta();
    try {
        [estado, lecciones] = await Promise.all([
            api('/estado'),
            api('/lecciones'),
        ]);
        elemento('#storage').textContent = estado.almacen;
        reiniciarTutor();
        usuario = (await api('/sesion')).usuario;
        actualizarSesion();
        dibujarLecciones();
        if (vista === 'finanzas') cargarFinanzas();
        navegar(location.hash.slice(1) || 'inicio');
    } catch (e) {
        aviso(
            'No se pudo conectar con C#. Ejecuta Vantex desde Visual Studio. ' +
                e.message,
            true,
        );
    }
}
setInterval(() => {
    if (document.hidden) return;
    if (vista === 'mercados' && seccionMercado === 'quotes') cargarMercado();
    if (vista === 'finanzas') cargarFinanzas();
    else if (usuario && ['inicio', 'portafolio'].includes(vista))
        cargarCuenta();
}, 20000);
iniciar();
