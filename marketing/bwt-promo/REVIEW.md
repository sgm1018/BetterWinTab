# Revisión del montaje

## Decisiones
48 s, 1080p/30 fps, texto en español y pantallas originales. Ruta «Conserva el contexto». Se descartaron el relato inverso de una llamada y la coreografía exclusivamente de teclado; detalles en STORYLINES.md.

El verde está tomado del logotipo. Las escenas oscuras corresponden al producto y al editor; las escenas claras separan problemas, contexto y cierre. No hay pantallas inventadas de Calls. Sus escenas explican capacidades verificadas en el código. El documento mostrado en Notes contiene un ejemplo ficticio.

## Revisión visual
Se inspeccionaron las hojas review-beats.jpg, review-phone.jpg y la transición de Notes a Calls. Se revisaron por separado las escenas de contexto corregidas y el cierre en review-fixed/contact-sheet.jpg.

P0 · 11–14 s · Escena de contexto en blanco por identificador duplicado entre el contenedor y una imagen → cambiado el sufijo del contenedor a -layer; recapturados 12.5 y 15.5 s, ambas escenas visibles.
P1 · 17–24 s · Texto de notas pequeño y corte del margen izquierdo → captura del editor original al 160 %, imagen sin desplazamiento horizontal. Paneo final para revelar la tabla.
P2 · Efectos · Ranura de 0.70 s para un archivo de 0.57 s → corregidas todas a 0.57 s.

La primera comprobación pasó con cero errores. Se conservó la advertencia de imagen duplicada en el hook: son dos recortes intencionalmente distintos del mismo escritorio, no una descarga repetida accidental.

## Límites
La escena de Calls es la más débil como demostración: describe el flujo con ondas y líneas editoriales porque la captura nativa falló. Una grabación real consentida, con una llamada ficticia y una generación real, permitiría sustituirla por una demostración sin cambiar el guion.

No se ha probado de extremo a extremo una llamada o un proveedor de IA. No se promete diarización individual: se indica explícitamente separación por fuente. Los informes requieren API key del usuario.

Audio: efectos de la biblioteca incluida en HyperFrames; sin música ni voz. La exportación se verificará mediante análisis de señal y metadatos, no mediante escucha humana. El ritmo no se ha evaluado con escucha humana.

## Archivos de revisión
Los PNG y hojas completas están en Z:/BetterWinTab-video/review y Z:/BetterWinTab-video/review-fixed para evitar llenar C:. La exportación se guarda junto a ellos. check-final.json conserva la validación de la composición.

Validación final de composición: OK. 0 errores de lint/runtime/layout, 0 avisos de runtime y 17/17 comprobaciones de contraste superadas. Se mantiene solo el aviso intencionado de reutilización de la captura del hook.
