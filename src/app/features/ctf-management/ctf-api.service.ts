import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

// This is the front-end shape for the user object returned by the API.
export interface CurrentUser {
  id: string;
  email: string;
  role: 'Admin' | 'Instructor';
}

export interface CtfInput {
  title: string;
  description: string;
  category: string;
  startsAt: string;
  endsAt: string;
}

export interface CtfRecord extends CtfInput {
  id: string;
  createdAt: string;
  createdBy: string;
}

// This Angular service is basically the API wrapper for the entire app.
// It handles sign-in, session checks, logout, and all CTF-related requests.
@Injectable({ providedIn: 'root' })
export class CtfApiService {
  private readonly http = inject(HttpClient);
  private csrfToken = '';

  // Send the login request and expect the current user back if the credentials are valid.
  login(email: string, password: string): Observable<CurrentUser> {
    return this.http.post<CurrentUser>('/api/auth/login', { email, password }, { withCredentials: true }).pipe(
      catchError((error: HttpErrorResponse) => this.asError(error)),
    );
  }

  session(): Observable<CurrentUser> {
    return this.http.get<CurrentUser>('/api/auth/session', { withCredentials: true }).pipe(
      catchError((error: HttpErrorResponse) => this.asError(error)),
    );
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}, this.writeOptions()).pipe(
      catchError((error: HttpErrorResponse) => this.asError(error)),
    );
  }

  listCtfs(): Observable<CtfRecord[]> {
    return this.http.get<CtfRecord[]>('/api/ctfs', { withCredentials: true }).pipe(
      catchError((error: HttpErrorResponse) => this.asError(error)),
    );
  }

  createCtf(ctf: CtfInput): Observable<CtfRecord> {
    return this.http.post<CtfRecord>('/api/ctfs', ctf, this.writeOptions()).pipe(
      catchError((error: HttpErrorResponse) => this.asError(error)),
    );
  }

  importCtfs(ctfs: CtfInput[]): Observable<CtfRecord[]> {
    return this.http.post<CtfRecord[]>('/api/ctfs/import', { ctfs }, this.writeOptions()).pipe(
      catchError((error: HttpErrorResponse) => this.asError(error)),
    );
  }

  createInstructor(email: string, password: string): Observable<CurrentUser> {
    return this.http.post<CurrentUser>('/api/admin/instructors', { email, password }, this.writeOptions()).pipe(
      catchError((error: HttpErrorResponse) => this.asError(error)),
    );
  }

  private writeOptions() {
    this.readCsrfToken();
    return {
      withCredentials: true,
      headers: new HttpHeaders({ 'X-CSRF-Token': this.csrfToken }),
    };
  }

  private readCsrfToken() {
    if (typeof document === 'undefined') return;
    const cookie = document.cookie.split('; ').find((part) => part.startsWith('ctf_csrf='));
    this.csrfToken = cookie ? decodeURIComponent(cookie.slice('ctf_csrf='.length)) : '';
  }

  private asError(error: HttpErrorResponse) {
    const message = typeof error.error?.message === 'string'
      ? error.error.message
      : error.status === 0
        ? 'CTF API is offline. In VS Code, run the “Start CTF Portal” task, then try again.'
        : error.status === 401
        ? 'Sign in to continue.'
        : error.status === 403
          ? 'Your account is not permitted to do that.'
          : 'The request could not be completed. Check the API connection and try again.';
    return throwError(() => new Error(message));
  }
}
