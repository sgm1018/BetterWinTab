# Investigación del producto

Fuentes: README.md completo, NotesPlugin/README.md, Assets/NotesEditor, Models, Services y ViewModels, incluida la implementación local sin commit de Calls. Revisado el 23 de octubre de 2026.

## Propósito
Navegador nativo de ventanas para Windows, orientado al teclado y al contexto. Resuelve la lista plana y cambiante de Alt + Tab mediante búsqueda, carpetas y accesos rápidos. C#/.NET 8/WinUI 3, licencia MIT, Windows 10/11.

## Funciones revisadas
- Overlay Ctrl + Tab configurable, navegación por teclado, activar/cerrar ventanas y miniaturas.
- Smart Folders por reglas de proceso/clase, carpetas manuales y arrastre.
- Búsqueda tolerante a errores por título, app, proceso y subtítulo. Reordenación semántica local opcional con multilingual-e5-small; descarga previa de modelos.
- Lanzador de aplicaciones instaladas.
- Pestañas Chrome, Edge y Brave mediante extensión y host nativo; Firefox todavía sin puente.
- Portapapeles de texto/imágenes y elementos fijados, solo durante la sesión.
- Editor de notas enriquecidas: Markdown pegado, encabezados, listas, citas, bloques de código, tablas, enlaces, imágenes pegadas o arrastradas y persistencia.
- Plugin local de notas para Codex: buscar, leer, crear y actualizar; control de versión para evitar sobrescribir cambios. No necesita API key propia.
- Papelera, temas y colores personalizables, ajustes y onboarding.
- Calls: captura de micrófono y salida del equipo en pistas distintas, cuenta atrás, biblioteca y persistencia; grabación mientras se oculta el overlay.
- ASR local: ruta de ejecución Moonshine Spanish Base, descarga/importación, transcripción por bloques de 8 s y transcripción al detener. La existencia de otros modelos en un catálogo no prueba que funcionen.
- Separación por fuente «You» / «Call audio». **No hay diarización individual de varios interlocutores remotos.** La interfaz declara esta limitación en CallsViewModel.TranscribeRecordingAsync y DiarizationStatus.
- Informes mediante proveedor remoto elegido: OpenAI, Anthropic, DeepSeek u OpenRouter. Requieren la API key del usuario. Plantillas Meeting notes, Action items, Decisions and open questions, Follow-up email y orden personalizada. Persistencia de documentos generados. Se envía texto al proveedor, no debe venderse como IA íntegramente offline.

## Evidencia y límites
- README es la fuente de las capturas windows/folders/clipboard y de compatibilidad/licencia.
- notes.png captura el editor HTML real con contenido ficticio de demostración, sin leer notas privadas.
- CallsViewModel.cs, Services/AudioCaptureService.cs, Services/Calls/Ai y Models/Calls verifican grabación, transcripción y generación.
- docs/superpowers/specs/2026-10-22-calls-recordings-design.md describe una propuesta más amplia; no se usa como prueba de implementación.
- No se efectuó una llamada real ni una generación con API de pago. Las escenas de Calls son explicaciones editoriales, no capturas o resultados fingidos.
- La compilación local pasó con 0 errores y dos advertencias de conflictos de versiones de dependencias. Eso no acredita una prueba completa de cada función.

## Procedencia de recursos
assets/windows.png: https://github.com/user-attachments/assets/7fb79db3-d2de-4727-9e70-bd1fa9178efa
assets/folders.png: https://github.com/user-attachments/assets/57191f88-bfaf-4539-8971-cbb85b66d13c
assets/clipboard.png: https://github.com/user-attachments/assets/1697cb59-17ac-49ac-8701-df1290018fe2
assets/logo.png: Assets/logos/betterWinTab.png del repositorio.
assets/notes.png: Assets/NotesEditor/editor.html, contenido ficticio; capture-notes.mjs.
assets/gsap.min.js: GSAP 3.14.2 distribuido por jsDelivr.
assets/{whoosh,impact,key}.mp3: biblioteca de sonidos incluida en HyperFrames 0.8.188.
