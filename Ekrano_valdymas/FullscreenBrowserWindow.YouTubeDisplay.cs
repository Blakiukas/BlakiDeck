using System.Text.Json;
using System.Threading;
namespace Ekrano_valdymas;
internal sealed partial class FullscreenBrowserWindow
{
 private CancellationTokenSource? displayRestore;
 private static bool IsYouTubePage(Uri? uri)=>uri?.Scheme=="https"&&(uri.Host=="youtube.com"||uri.Host.EndsWith(".youtube.com",StringComparison.OrdinalIgnoreCase));
 private async void RestoreYouTubeDisplay()
 {
  displayRestore?.Cancel();displayRestore?.Dispose();displayRestore=new();var token=displayRestore.Token;
  if(browserClosed||!IsYouTubePage(webView.Source))return;
  try {
   for(int i=0;i<30&&!browserClosed&&!token.IsCancellationRequested;i++) {
    if(!IsYouTubePage(webView.Source))return;
    string result=await webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",JsonSerializer.Serialize(new {expression="window.__blakiDeckRestoreDisplay?.()",userGesture=true,awaitPromise=true,returnByValue=true}));
    using var parsed=JsonDocument.Parse(result);
    if(parsed.RootElement.TryGetProperty("result",out var r)&&r.TryGetProperty("value",out var value)&&value.ValueKind==JsonValueKind.String) {
     if(value.GetString()=="done")return;
     // A new WebView may leave focus on the toolbar. Chromium requires a
     // focused document for fullscreen, even with a user-gesture evaluation.
     // Never activate another window or interrupt address/menu editing.
     if(value.GetString()=="needs-focus"&&IsActive&&!address.IsKeyboardFocusWithin&&webSettingsMenu?.IsOpen!=true)
      webView.Focus();
    }
    await Task.Delay(400,token);
   }
  }catch(OperationCanceledException){}
  catch(Exception e){if(!browserClosed)InteractionDiagnostics.RecordFailure("YouTube display restore",e);}
 }
 internal const string YouTubeDisplayScript="""
 (() => {
  if (window !== top || !/(^|\.)youtube\.com$/.test(location.hostname)) return;
  const prefix='blakideck.display.v1:';
  let key='', wanted=null, restoring=false, deadline=0, lastFullscreenIntent=-Infinity, rememberedFullscreen=false;
  const videoKey=()=>{const u=new URL(location.href);return u.pathname==='/watch'&&u.searchParams.get('v')?prefix+u.searchParams.get('v'):'';};
  function begin() {
   const next=videoKey(); if(next===key)return;
   key=next;wanted=null;try{wanted=JSON.parse(localStorage.getItem(key)||'null');}catch{}
   if(!wanted||typeof wanted.fullscreen!=='boolean'||typeof wanted.theater!=='boolean')wanted=null;
   rememberedFullscreen=wanted?.fullscreen??false;
   restoring=!!wanted;deadline=performance.now()+11500;
  }
  function save() {
   begin();const watch=document.querySelector('ytd-watch-flexy');if(!key||!watch||restoring||document.visibilityState!=='visible')return;
   const chat=document.querySelector('ytd-live-chat-frame');
   // Closing/deactivating the host can exit native fullscreen. Layout updates
   // must retain the user's choice instead of sampling that transient exit.
   try{localStorage.setItem(key,JSON.stringify({theater:watch.hasAttribute('theater'),fullscreen:rememberedFullscreen,chatCollapsed:chat?chat.hasAttribute('collapsed'):null}));}catch{}
  }
  window.__blakiDeckRestoreDisplay=async()=>{
   begin();if(!key||!wanted)return 'done';
   const watch=document.querySelector('ytd-watch-flexy'),player=document.getElementById('movie_player');
   if(!watch||!player)return 'pending';
   let pending=false;
   if(watch.hasAttribute('theater')!==wanted.theater){const button=player.querySelector('.ytp-size-button');if(button)button.click();pending=true;}
   if(typeof wanted.chatCollapsed==='boolean'){
    const chat=document.querySelector('ytd-live-chat-frame');
    if(!chat)pending=true;
    else if(chat.hasAttribute('collapsed')!==wanted.chatCollapsed){const button=chat.querySelector('#show-hide-button button');if(button)button.click();pending=true;}
   }
   if(wanted.fullscreen&&!document.fullscreenElement){
    if(!document.hasFocus())return 'needs-focus';
    try{await player.requestFullscreen();}catch{pending=true;}
   }
   if(pending&&performance.now()<deadline)return 'pending';
   restoring=false;wanted=null;return 'done';
  };
  document.addEventListener('click',event=>{
   const target=event.target instanceof Element?event.target:null;
   if(!target?.closest('.ytp-size-button,.ytp-fullscreen-button,ytd-live-chat-frame #show-hide-button'))return;
   // Explicit user input wins over a pending automatic restore.
   if(event.isTrusted){restoring=false;wanted=null;if(target.closest('.ytp-fullscreen-button'))lastFullscreenIntent=performance.now();setTimeout(save,500);}
  },true);
  document.addEventListener('keydown',event=>{if(event.isTrusted&&['Escape','f','t'].includes(event.key)){restoring=false;wanted=null;if(event.key!=='t')lastFullscreenIntent=performance.now();setTimeout(save,500);}},true);
  document.addEventListener('fullscreenchange',()=>{
   if(document.fullscreenElement||performance.now()-lastFullscreenIntent<1800){
    rememberedFullscreen=!!document.fullscreenElement;
    lastFullscreenIntent=-Infinity;
    save();
   }
  });
  function observeLayout(){if(!document.documentElement)return;new MutationObserver(changes=>{if(changes.some(change=>change.target.matches?.('ytd-watch-flexy,ytd-live-chat-frame')))save();}).observe(document.documentElement,{subtree:true,attributes:true,attributeFilter:['theater','collapsed']});}
  if(document.documentElement)observeLayout();else document.addEventListener('DOMContentLoaded',observeLayout,{once:true});
  document.addEventListener('yt-navigate-finish',begin);
  begin();
 })();
 """;
}
