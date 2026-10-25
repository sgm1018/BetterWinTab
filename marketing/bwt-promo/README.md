# BetterWinTab — vídeo promocional

Película editable en HyperFrames, en español, 48 segundos, 1920 × 1080 y 30 fps.

- Previsualización: http://localhost:3027/#project/bwt-promo
- Exportación: Z:/BetterWinTab-video/BetterWinTab-promo-es.mp4
- Investigación: FEATURES.md
- Guion y criterio visual: STORYLINES.md y SPEC.md
- Revisión: REVIEW.md
- Composición: index.html y compositions/

Las capturas proceden del README y del editor real de notas con datos ficticios. Los segmentos de llamadas son explicaciones de capacidades comprobadas en el código, no una grabación de llamadas ni respuestas generadas simuladas. No se han usado notas privadas.

## Editar y renderizar

Los comandos de package.json fijan HyperFrames 0.8.188. Requieren Node.js, Chrome y FFmpeg/FFprobe. En este equipo FFmpeg portátil está en Z:/BetterWinTab-video/tools/ffmpeg-9.0.2-essentials_build/bin. Usar Z:/BetterWinTab-video/temp como TEMP/TMP por el escaso espacio libre en C:.

```powershell
$env:TEMP = 'Z:\BetterWinTab-video\temp'
$env:TMP = $env:TEMP
$env:PATH = 'Z:\BetterWinTab-video\tools\ffmpeg-9.0.2-essentials_build\bin;' + $env:PATH
npm run check
npm run dev
npm run render -- --quality delivery --workers 1 --output Z:\BetterWinTab-video\BetterWinTab-promo-es.mp4
```

El editor visual modifica index.html y las escenas directamente. build-film.mjs conserva la versión inicial reproducible: **no ejecutarlo después de editar a mano sin conservar los cambios**, porque regenera las escenas. capture-notes.mjs es el script de procedencia de la captura del editor.

Esta versión contiene efectos de sonido, sin música de fondo ni locución.
