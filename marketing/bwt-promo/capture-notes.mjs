import puppeteer from 'file:///C:/Users/sergio.gonzalez/AppData/Local/npm-cache/_npx/702923228c2ce1e6/node_modules/puppeteer-core/lib/puppeteer/puppeteer-core.js';
const browser=await puppeteer.launch({executablePath:'C:/Users/sergio.gonzalez/.cache/hyperframes/chrome/chrome-headless-shell/win64-152.0.7977.30/chrome-headless-shell-win64/chrome-headless-shell.exe',headless:true,userDataDir:'Z:/BetterWinTab-video/temp/notes-chrome',args:['--no-sandbox']});
const p=await browser.newPage(); await p.setViewport({width:840,height:900,deviceScaleFactor:1});
await p.goto('file:///C:/Users/sergio.gonzalez/Desktop/Yo/Mio/BetterWinTab/Assets/NotesEditor/editor.html');
await p.evaluate(()=>{document.body.className='font-sans';document.body.style.zoom='1.6';document.querySelector('#title').textContent='Lanzamiento del proyecto';document.querySelector('#editor').innerHTML='<h2>Lo importante, a mano</h2><p>Un espacio para guardar ideas y darles forma.</p><blockquote>Volver al trabajo con el contexto intacto.</blockquote><h2>Antes de la llamada</h2><ul><li>Revisar la propuesta</li><li>Preparar las preguntas del equipo</li><li>Guardar enlaces y referencias</li></ul><h2>Próximos pasos</h2><table><tbody><tr><th>Acción</th><th>Estado</th></tr><tr><td>Revisar el diseño</td><td>Pendiente</td></tr><tr><td>Compartir conclusiones</td><td>Pendiente</td></tr></tbody></table>';});
await p.screenshot({path:'assets/notes.png'});await browser.close();



