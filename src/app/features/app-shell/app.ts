import { Component, OnInit, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CtfApiService, CtfInput, CtfRecord, CurrentUser } from '../ctf-management/ctf-api.service';

@Component({
  imports: [FormsModule],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
// This is the main page component for the app.
// It handles sign-in, sign-out, rendering the user, creating CTFs, and importing CTF JSON files.
export class App implements OnInit {
  private readonly api = inject(CtfApiService);
  private readonly platformId = inject(PLATFORM_ID);

  user: CurrentUser | null = null;
  ctfs: CtfRecord[] = [];
  email = '';
  password = '';
  ctfTitle = '';
  ctfDescription = '';
  ctfCategory = 'Jeopardy';
  startsAt = '';
  endsAt = '';
  instructorEmail = '';
  instructorPassword = '';
  isBusy = false;
  notice = '';
  error = '';

  // On startup, the app asks the API if a user is already signed in from a valid cookie.
  ngOnInit() {
    if (!isPlatformBrowser(this.platformId)) return;
    this.api.session().subscribe({
      next: (user) => {
        this.user = user;
        this.loadCtfs();
      },
    });
  }

  get isAdmin() {
    return this.user?.role === 'Admin';
  }

  signIn() {
    this.clearMessages();
    this.isBusy = true;
    this.api.login(this.email, this.password).subscribe({
      next: (user) => {
        this.user = user;
        this.password = '';
        this.isBusy = false;
        this.loadCtfs();
        this.notice = 'Signed in successfully.';
      },
      error: (error: Error) => this.fail(error),
    });
  }

  signOut() {
    this.clearMessages();
    this.api.logout().subscribe({
      next: () => {
        this.user = null;
        this.ctfs = [];
        this.notice = 'You are signed out.';
      },
      error: (error: Error) => this.fail(error),
    });
  }

  createCtf() {
    this.clearMessages();
    const input = this.buildCtfInput();
    if (!input) return;
    this.isBusy = true;
    this.api.createCtf(input).subscribe({
      next: () => {
        this.resetCtfForm();
        this.isBusy = false;
        this.notice = 'CTF created.';
        this.loadCtfs();
      },
      error: (error: Error) => this.fail(error),
    });
  }

  async importFile(event: Event) {
    this.clearMessages();
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    input.value = '';
    if (file.size > 1_048_576) {
      this.error = 'Choose a JSON file smaller than 1 MB.';
      return;
    }

    try {
      const parsed: unknown = JSON.parse(await file.text());
      const records = Array.isArray(parsed)
        ? parsed
        : typeof parsed === 'object' && parsed !== null && 'ctfs' in parsed
          ? (parsed as { ctfs: unknown }).ctfs
          : [parsed];
      if (!Array.isArray(records) || records.length === 0 || records.length > 50) {
        throw new Error('Provide one CTF object or an array of up to 50 CTF objects.');
      }
      const ctfs = records.map((record) => this.normalizeImportedCtf(record));
      this.isBusy = true;
      this.api.importCtfs(ctfs).subscribe({
        next: (created) => {
          this.isBusy = false;
          this.notice = `${created.length} CTF${created.length === 1 ? '' : 's'} imported.`;
          this.loadCtfs();
        },
        error: (error: Error) => this.fail(error),
      });
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'The selected file is not valid CTF JSON.';
    }
  }

  provisionInstructor() {
    this.clearMessages();
    this.isBusy = true;
    this.api.createInstructor(this.instructorEmail, this.instructorPassword).subscribe({
      next: () => {
        this.instructorEmail = '';
        this.instructorPassword = '';
        this.isBusy = false;
        this.notice = 'Instructor account created.';
      },
      error: (error: Error) => this.fail(error),
    });
  }

  formatDate(value: string) {
    return new Date(value).toLocaleString();
  }

  private loadCtfs() {
    this.api.listCtfs().subscribe({
      next: (ctfs) => this.ctfs = ctfs,
      error: (error: Error) => this.error = error.message,
    });
  }

  private buildCtfInput(): CtfInput | null {
    if (!this.startsAt || !this.endsAt) {
      this.error = 'Choose both a start and end time.';
      return null;
    }
    const startsAt = new Date(this.startsAt);
    const endsAt = new Date(this.endsAt);
    if (endsAt <= startsAt) {
      this.error = 'The end time must be after the start time.';
      return null;
    }
    return {
      title: this.ctfTitle.trim(),
      description: this.ctfDescription.trim(),
      category: this.ctfCategory,
      startsAt: startsAt.toISOString(),
      endsAt: endsAt.toISOString(),
    };
  }

  private normalizeImportedCtf(record: unknown): CtfInput {
    if (typeof record !== 'object' || record === null) {
      throw new Error('Each imported CTF must be a JSON object.');
    }
    const value = record as Record<string, unknown>;
    const title = value['title'];
    const description = value['description'];
    const category = value['category'];
    const startsAt = value['startsAt'] ?? value['startTime'];
    const endsAt = value['endsAt'] ?? value['endTime'];
    if ([title, description, category, startsAt, endsAt].some((field) => typeof field !== 'string')) {
      throw new Error('Each CTF needs title, description, category, startsAt, and endsAt text fields.');
    }
    const startDate = new Date(startsAt as string);
    const endDate = new Date(endsAt as string);
    if (Number.isNaN(startDate.getTime()) || Number.isNaN(endDate.getTime())) {
      throw new Error('CTF start and end times must be valid date strings.');
    }
    return {
      title: title as string,
      description: description as string,
      category: category as string,
      startsAt: startDate.toISOString(),
      endsAt: endDate.toISOString(),
    };
  }

  private resetCtfForm() {
    this.ctfTitle = '';
    this.ctfDescription = '';
    this.startsAt = '';
    this.endsAt = '';
  }

  private clearMessages() {
    this.notice = '';
    this.error = '';
  }

  private fail(error: Error) {
    this.isBusy = false;
    this.error = error.message;
  }
}
