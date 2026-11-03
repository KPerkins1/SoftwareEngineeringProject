import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

// Start the Angular app in the browser with the providers from appConfig.
bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
