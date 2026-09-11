import {Deck} from '../../Client/node_modules/@deck.gl/core/dist/index.js';
import {ScatterplotLayer, PathLayer, IconLayer, TextLayer} from '../../Client/node_modules/@deck.gl/layers/dist/index.js';
import {createScene} from '../../Client/Scripts/fleetMap/rendering/scene.js';

const host = document.querySelector('#map'), status = document.querySelector('#status'), output = document.querySelector('#results');
const results = [];
const percentile = (values, p) => values.slice().sort((a,b) => a-b)[Math.max(0, Math.ceil(values.length*p)-1)] ?? null;
const waitFrame = () => new Promise(requestAnimationFrame);

document.querySelector('#run').onclick = async event => {
  event.target.disabled = true;
  try {
    for (const count of [30, 100, 300, 1000]) {
      status.textContent = `Running ${count} trucks: warmup + 12 seconds of motion, camera changes and picking`;
      let deck, metrics = [], measuring = false, draws = 0, activeQuery = null;
      const gpuSamples = [], queries = [];
      class Overlay {
        constructor(props) {
          deck = new Deck({...props, parent: host, width: 1000, height: 600,
            initialViewState: {longitude: -97, latitude: 38, zoom: 5, pitch: 0, bearing: 0},
            controller: false, _onMetrics: value => metrics.push({...value}),
            onBeforeRender: ({gl}) => {
              if (!measuring) return;
              const ext=gl.getExtension('EXT_disjoint_timer_query_webgl2');
              if (!ext) return;
              if (gl.getParameter(ext.GPU_DISJOINT_EXT)) {
                queries.splice(0).forEach(q=>gl.deleteQuery(q)); gpuSamples.length=0; return;
              }
              while (queries.length && gl.getQueryParameter(queries[0],gl.QUERY_RESULT_AVAILABLE)) {
                const q=queries.shift(); gpuSamples.push(gl.getQueryParameter(q,gl.QUERY_RESULT)/1e6); gl.deleteQuery(q);
              }
              if (draws%10===0 && queries.length<16 && !gl.getQuery(ext.TIME_ELAPSED_EXT,gl.CURRENT_QUERY)) {
                activeQuery=gl.createQuery(); gl.beginQuery(ext.TIME_ELAPSED_EXT,activeQuery);
              }
            },
            onAfterRender: ({gl}) => {
              if(measuring) draws++;
              if(activeQuery) {gl.endQuery(gl.getExtension('EXT_disjoint_timer_query_webgl2').TIME_ELAPSED_EXT);queries.push(activeQuery);activeQuery=null;}
            },
          });
        }
        setMap() {}
        setProps(props) { deck.setProps(props); }
        finalize() { deck.finalize(); }
      }
      const map = {getDiv: () => host, setOptions() {}, addListener: () => ({remove() {}})};
      const scene = createScene(map, {GoogleMapsOverlay: Overlay, ScatterplotLayer, PathLayer, IconLayer, TextLayer});
      const stations = scene.createStationPointLayer(map, () => {});
      for (let i=0;i<2000;i++) stations.setPoint(String(i), {lng: -110+(i%100)*.25, lat: 31+Math.floor(i/100)*.7}, 'rgb(50,160,90)', false, false);
      stations.setVisible(true);
      const route = new scene.Polyline({map, strokeWeight: 4, strokeColor: '#2e50e7'});
      route.setPath(Array.from({length:2000},(_,i)=>({lng:-105+i*.008,lat:38+Math.sin(i/100)*.2})));
      const labels=[];
      for(let i=0;i<2;i++) {
        const stop = new scene.StopMarker({position:{lng:-100+i*5,lat:38},number:String(i+1)});
        stop.setDistance('500 mi · 805 km');
        labels.push(stop);
      }
      const trucks=Array.from({length:count},(_,i)=> {
        const marker=scene.createTruckMarker(map,()=>{}); marker.update({unitNumber:String(10000+i),engineState:'On'});
        marker.render({longitude:-105+(i%50)*.3,latitude:34+Math.floor(i/50)*.25,heading:90}); return marker;
      });
      const warmUntil=performance.now()+2500; while(performance.now()<warmUntil) await waitFrame();
      const gl=host.querySelector('canvas').getContext('webgl2');
      const rendererInfo=gl?.getExtension('WEBGL_debug_renderer_info');
      const renderer=rendererInfo?gl.getParameter(rendererInfo.UNMASKED_RENDERER_WEBGL):'Unavailable';
      const longTasks=[];
      const observer=new PerformanceObserver(list=>longTasks.push(...list.getEntries().map(x=>x.duration)));
      if(PerformanceObserver.supportedEntryTypes.includes('longtask')) observer.observe({type:'longtask'});
      metrics=[]; measuring=true;
      const intervals=[], work=[], start=performance.now(); let previous=start, frame=0;
      while(performance.now()-start<12000) {
        const at=await waitFrame(); intervals.push(at-previous); previous=at;
        const before=performance.now(), phase=(at-start)/1000;
        trucks.forEach((truck,i)=>truck.render({longitude:-105+(i%50)*.3+phase*.002,latitude:34+Math.floor(i/50)*.25,heading:90}));
        deck.setProps({viewState:{longitude:-97+Math.sin(phase)*.2,latitude:38,zoom:6+Math.sin(phase*.7),pitch:0,bearing:0}});
        if(frame%30===0) deck.pickObject({x:500,y:300,radius:5});
        if(frame%60===0) {labels[0].setDistance(`${500-Math.floor(phase)} mi · 805 km`); trucks[0].setSelected(frame%120===0);}
        work.push(performance.now()-before); frame++;
      }
      longTasks.push(...observer.takeRecords().map(x=>x.duration)); observer.disconnect(); measuring=false;
      const result={trucks:count,stations:2000,routePoints:2000,elapsedMs:performance.now()-start,frames:frame,
        frameIntervalP50Ms:percentile(intervals,.5),frameIntervalP95Ms:percentile(intervals,.95),frameIntervalP99Ms:percentile(intervals,.99),
        intervalsOver33ms:intervals.filter(x=>x>33.34).length,longTasks:longTasks.length,maxLongTaskMs:Math.max(0,...longTasks),
        inputWorkP95Ms:percentile(work,.95),renderer,pixelRatio:devicePixelRatio,
        deckCpuPerFrameP95Ms:percentile(metrics.map(x=>x.cpuTimePerFrame),.95),
        drawnFrames:draws,gpuSamples:gpuSamples.length,gpuDrawP50Ms:percentile(gpuSamples,.5),gpuDrawP95Ms:percentile(gpuSamples,.95),
        reportedGpuMemoryBytes:Math.max(0,...metrics.map(x=>x.gpuMemory)) || null,
        gpuTimerSupported:!!gl?.getExtension('EXT_disjoint_timer_query_webgl2'),
        visibility:document.visibilityState};
      results.push(result); output.textContent=JSON.stringify(results,null,2);
      queries.forEach(q=>gl.deleteQuery(q));
      scene.dispose(); host.replaceChildren();
      await waitFrame();
    }
    status.textContent='Complete. Results are isolated WebGL layer measurements, not full Google Maps frame times.';
  } catch(error) {status.textContent=`ERROR: ${error.stack || error}`;}
  finally {event.target.disabled=false;}
};
