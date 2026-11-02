import { HttpClient } from '@angular/common/http';
import { afterNextRender, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface CurrentUser {
  id: string;
  email: string;
  name: string;
}

@Component({
  imports: [FormsModule],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = 'http://localhost:5000/api/auth';

  protected readonly user = signal<CurrentUser | null>(null);
  protected readonly checkingSession = signal(true);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');
  protected email = 'demo@example.com';
  protected password = '';

  constructor() {
    afterNextRender(() => this.checkSession());
  }

  protected login(): void {
    this.busy.set(true);
    this.errorMessage.set('');

    this.http
      .post<CurrentUser>(
        `${this.apiUrl}/login`,
        { email: this.email, password: this.password },
        { withCredentials: true },
      )
      .subscribe({
        next: (user) => {
          this.user.set(user);
          this.password = '';
          this.busy.set(false);
        },
        error: () => {
          this.errorMessage.set('That email and password did not match.');
          this.busy.set(false);
        },
      });
  }

  protected logout(): void {
    this.busy.set(true);
    this.http
      .post(`${this.apiUrl}/logout`, {}, { withCredentials: true })
      .subscribe({
        next: () => {
          this.user.set(null);
          this.busy.set(false);
        },
        error: () => {
          this.errorMessage.set('Could not sign out. Please try again.');
          this.busy.set(false);
        },
      });
  }

  private checkSession(): void {
    this.http
      .get<CurrentUser>(`${this.apiUrl}/me`, { withCredentials: true })
      .subscribe({
        next: (user) => {
          this.user.set(user);
          this.checkingSession.set(false);
        },
        error: () => this.checkingSession.set(false),
      });
  }
}
