# BetterWinTab Notes

Complemento local para crear y editar notas de BetterWinTab desde Codex en Windows. Incluye el servidor MCP y un puente en la aplicación. Necesita Node.js 22 o posterior; la app debe estar abierta en el mismo equipo. No necesita un servicio web ni claves API.

## Activar

El complemento se instala con:

```powershell
.\NotesPlugin\Install.ps1
```

Para cargar el puente nuevo, cierra la versión anterior desde **Settings → General → Close BetterWinTab**. Después ejecuta:

```powershell
.\NotesPlugin\Start-BetterWinTab.ps1
```

El script compila una copia en `bin/NotesPluginCheck`, conserva los datos existentes en `%AppData%/BetterWinTab` y abre la app. Rechaza iniciarse si hay otra instancia abierta para evitar dos copias editando las mismas notas.

Abre un nuevo chat de Codex y selecciona **BetterWinTab Notes**. Referencia del complemento: `betterwintab-notes@betterwintab-local`.

Ejemplos:

- «Crea una nota titulada Ideas con estos tres puntos…»
- «Busca la nota del proyecto y añade al final…»
- «Cambia el título de la nota Reunión a Reunión del lunes»

## Herramientas

| Herramienta | Acción |
| --- | --- |
| `status` | Comprueba la conexión con la app. |
| `list_notes` | Busca por título o contenido, con paginación. |
| `get_note` | Devuelve el contenido, HTML y versión actual. |
| `list_folders` | Lista las carpetas existentes y sus IDs. |
| `create_note` | Crea y guarda una nota con título y texto. |
| `update_note` | Cambia título, cuerpo, fuente o carpeta; permite añadir texto al final. |

Las actualizaciones requieren `expectedUpdatedAt`, obtenido con `get_note`. Si el usuario ha editado la nota mientras tanto, se devuelve un conflicto. Los campos omitidos se conservan. `appendText` conserva el HTML existente; `content` sustituye el cuerpo por texto plano codificado como HTML. El texto añadido se trata como texto, sin ejecutar etiquetas. Las notas no pueden borrarse a través del complemento.

El puente usa la canalización Windows `BetterWinTab.Notes.v1`, restringida al usuario actual. Sincroniza los cambios pendientes del editor antes de leer o escribir, actualiza la lista y el editor, y solo confirma una escritura después de guardarla en disco. La app y el complemento deben ejecutarse con el mismo usuario de Windows.

## Verificación

Desde la raíz del repositorio:

```powershell
dotnet build NotesPlugin\tests\BridgeHarness\BridgeHarness.csproj
node --test NotesPlugin\tests\server.test.mjs
dotnet build BetterWinTab.csproj -p:Platform=x64 -p:OutDir=bin\NotesPluginCheck\
```

Las pruebas usan una carpeta temporal y una canalización única, sin tocar las notas personales ni el puente de una app abierta. Cubren descubrimiento MCP por stdio, conexión real Node–C#, creación, lectura, búsqueda, edición, persistencia, carpetas, texto con etiquetas y caracteres españoles, conflictos y validación.

`betterwintab-notes/plugin.json` y `mcp.json` contienen los manifiestos portables. `.codex-plugin/plugin.json` y `.mcp.json` son la compatibilidad con el cliente local. Al modificar la identidad, versión o presentación, sincroniza ambos manifiestos y reinstala desde el directorio fuente. No edites el caché instalado.

Este complemento necesita ejecución local; no proporciona acceso desde ChatGPT web o móvil.
