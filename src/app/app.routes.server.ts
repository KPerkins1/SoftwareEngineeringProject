import { RenderMode, ServerRoute } from '@angular/ssr';

// Prerender the app's current route when Angular builds the server output.
export const serverRoutes: ServerRoute[] = [
  {
    path: '**',
    renderMode: RenderMode.Prerender
  }
];
