import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

// These interfaces describe the JSON shapes that the API sends to the app.
interface CurrentUser {
  id: string;
  email: string;
  name: string;
}

interface PuzzleFile {
  id: string;
  fileName: string;
  size: number;
}

interface Challenge {
  id: string;
  name: string;
  description: string;
  points: number;
  fileName: string | null;
  fileSize: number | null;
  isSolved: boolean;
}

interface CtfEvent {
  id: string;
  name: string;
  description: string;
  createdAt: string;
  challenges: Challenge[];
}

interface FlagSubmissionResult {
  correct: boolean;
  message: string;
  points: number;
}

// This component handles sign-in and the CTF/challenge screens after sign-in.
@Component({
  imports: [FormsModule],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  private readonly http = inject(HttpClient);
  // The API runs separately from Angular during local development.
  private readonly apiUrl = 'http://localhost:5000/api';

  // Signals make the template update when a request changes the page state.
  protected readonly user = signal<CurrentUser | null>(null);
  protected readonly ctfs = signal<CtfEvent[]>([]);
  protected readonly checkingSession = signal(true);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly noticeMessage = signal('');
  protected readonly creatingChallengeCtfId = signal<string | null>(null);
  protected readonly submittingChallengeId = signal<string | null>(null);
  // These IDs decide which event or puzzle is currently showing an edit form.
  protected readonly editingCtfId = signal<string | null>(null);
  protected readonly editingChallengeId = signal<string | null>(null);
  protected readonly challengeFeedback = signal<Record<string, string>>({});
  protected email = 'demo@example.com';
  protected password = '';
  protected ctfName = '';
  protected ctfDescription = '';
  // These values hold the draft text while an organizer edits an event.
  protected editCtfName = '';
  protected editCtfDescription = '';
  protected challengeNames: Record<string, string> = {};
  protected challengeDescriptions: Record<string, string> = {};
  protected challengeFlags: Record<string, string> = {};
  protected challengePoints: Record<string, number> = {};
  protected flagAnswers: Record<string, string> = {};
  // These values hold the draft puzzle changes until the API saves them.
  protected editChallengeCtfId = '';
  protected editChallengeName = '';
  protected editChallengeDescription = '';
  protected editChallengeFlag = '';
  protected editChallengePoints = 100;
  protected removeChallengeFile = false;

  // Copy the current event values into the edit form before showing it.
  protected startEditCtf(ctf: CtfEvent): void {
    this.editingCtfId.set(ctf.id);
    this.editCtfName = ctf.name;
    this.editCtfDescription = ctf.description;
  }

  // Close the edit form without sending the draft values to the API.
  protected cancelEditCtf(): void {
    this.editingCtfId.set(null);
  }

  // Send the edited event details to the API.
  protected saveCtf(ctfId: string): void {
    const name = this.editCtfName.trim();
    const description = this.editCtfDescription.trim();
    if (!name) return;

    this.busy.set(true);
    this.errorMessage.set('');
    this.http
      .put<CtfEvent>(`${this.apiUrl}/ctfs/${ctfId}`, { name, description }, { withCredentials: true })
      .subscribe({
        next: (updatedCtf) => {
          // Only update the screen after the API confirms the save.
          this.ctfs.update((current) => current.map((ctf) => ctf.id === ctfId ? updatedCtf : ctf));
          this.editingCtfId.set(null);
          this.noticeMessage.set('CTF event updated.');
          this.busy.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.setApiError('update this CTF', error.status);
          this.busy.set(false);
        },
      });
  }

  // Delete an event after the organizer confirms the warning.
  protected deleteCtf(ctf: CtfEvent): void {
    // Ask first because deleting an event also deletes every puzzle in it.
    if (!globalThis.confirm(`Delete "${ctf.name}" and all of its puzzles and files? This cannot be undone.`)) return;

    this.busy.set(true);
    this.errorMessage.set('');
    this.http
      .delete(`${this.apiUrl}/ctfs/${ctf.id}`, { withCredentials: true })
      .subscribe({
        next: () => {
          // Remove the event from the page only after the server deletes it.
          this.ctfs.update((current) => current.filter((item) => item.id !== ctf.id));
          this.noticeMessage.set(`Deleted ${ctf.name}.`);
          this.busy.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.setApiError('delete this CTF', error.status);
          this.busy.set(false);
        },
      });
  }

  protected startEditChallenge(ctfId: string, challenge: Challenge): void {
    // A blank new-flag field means the organizer does not want to change the answer.
    this.editingChallengeId.set(challenge.id);
    this.editChallengeCtfId = ctfId;
    this.editChallengeName = challenge.name;
    this.editChallengeDescription = challenge.description;
    this.editChallengeFlag = '';
    this.editChallengePoints = challenge.points;
    this.removeChallengeFile = false;
  }

  // Close the puzzle editor and clear which puzzle was being edited.
  protected cancelEditChallenge(): void {
    this.editingChallengeId.set(null);
    this.editChallengeCtfId = '';
  }

  // Send changed puzzle fields and any replacement file in one request.
  protected saveChallenge(ctfId: string, challengeId: string, file: File | undefined): void {
    const name = this.editChallengeName.trim();
    const description = this.editChallengeDescription.trim();
    const flag = this.editChallengeFlag.trim();
    const points = Number(this.editChallengePoints);
    if (!name || !description || !Number.isInteger(points) || points < 1 || points > 10000) return;
    if (flag.length > 200) return;
    if (file && file.size > 25 * 1024 * 1024) {
      this.errorMessage.set('Puzzle files must be 25 MB or smaller.');
      return;
    }

    // FormData is needed because an edit may include a replacement file.
    const body = new FormData();
    body.append('name', name);
    body.append('description', description);
    body.append('flag', flag);
    body.append('points', String(points));
    body.append('removeFile', String(this.removeChallengeFile));
    if (file) body.append('file', file);

    this.busy.set(true);
    this.errorMessage.set('');
    this.http
      .put<Challenge>(`${this.apiUrl}/ctfs/${ctfId}/challenges/${challengeId}`, body, { withCredentials: true })
      .subscribe({
        next: (updatedChallenge) => {
          // Replace only this puzzle in the matching event after the API accepts the edit.
          this.ctfs.update((current) => current.map((ctf) => ctf.id === ctfId
            ? {
                ...ctf,
                challenges: ctf.challenges.map((challenge) => challenge.id === challengeId ? updatedChallenge : challenge),
              }
            : ctf,
          ));
          this.cancelEditChallenge();
          this.noticeMessage.set('Puzzle updated.');
          this.busy.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.setApiError('update this puzzle', error.status);
          this.busy.set(false);
        },
      });
  }

  // Delete one puzzle after confirmation, without removing its whole event.
  protected deleteChallenge(ctfId: string, challenge: Challenge): void {
    // Confirm before deleting the puzzle and any file attached to it.
    if (!globalThis.confirm(`Delete the puzzle "${challenge.name}" and its uploaded file? This cannot be undone.`)) return;

    this.busy.set(true);
    this.errorMessage.set('');
    this.http
      .delete(`${this.apiUrl}/ctfs/${ctfId}/challenges/${challenge.id}`, { withCredentials: true })
      .subscribe({
        next: () => {
          // Keep the event and its other puzzles, and remove only the selected puzzle.
          this.ctfs.update((current) => current.map((ctf) => ctf.id === ctfId
            ? { ...ctf, challenges: ctf.challenges.filter((item) => item.id !== challenge.id) }
            : ctf,
          ));
          this.noticeMessage.set(`Deleted ${challenge.name}.`);
          this.busy.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.setApiError('delete this puzzle', error.status);
          this.busy.set(false);
        },
      });
  }

  constructor() {
    // Wait until browser rendering before checking cookies, since SSR has no browser session.
    afterNextRender(() => this.checkSession());
  }

  protected login(): void {
    this.busy.set(true);
    this.errorMessage.set('');

    this.http
      .post<CurrentUser>(
        `${this.apiUrl}/auth/login`,
        { email: this.email, password: this.password },
        { withCredentials: true },
      )
      .subscribe({
        next: (user) => {
          // Load the user's events after the API confirms the login.
          this.user.set(user);
          this.loadCtfs();
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
      .post(`${this.apiUrl}/auth/logout`, {}, { withCredentials: true })
      .subscribe({
        next: () => {
          // Clear local event data so it is not shown after signing out.
          this.user.set(null);
          this.ctfs.set([]);
          this.busy.set(false);
        },
        error: () => {
          this.errorMessage.set('Could not sign out. Please try again.');
          this.busy.set(false);
        },
      });
  }

  protected createCtf(): void {
    const name = this.ctfName.trim();
    const description = this.ctfDescription.trim();
    if (!name) return;

    this.busy.set(true);
    this.errorMessage.set('');
    this.noticeMessage.set('');
    this.http
      .post<CtfEvent>(`${this.apiUrl}/ctfs`, { name, description }, { withCredentials: true })
      .subscribe({
        next: (ctf) => {
          // Add the server's new event to the top of the list without reloading the page.
          this.ctfs.update((current) => [ctf, ...current]);
          this.challengePoints[ctf.id] = 100;
          this.ctfName = '';
          this.ctfDescription = '';
          this.noticeMessage.set('CTF created. Add a puzzle to get started.');
          this.busy.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.setApiError('create this CTF', error.status);
          this.busy.set(false);
        },
      });
  }

  protected createChallenge(ctfId: string, file: File | undefined): void {
    // These values come from separate forms for each CTF, so they are keyed by event ID.
    const name = this.challengeNames[ctfId]?.trim() ?? '';
    const description = this.challengeDescriptions[ctfId]?.trim() ?? '';
    const flag = this.challengeFlags[ctfId]?.trim() ?? '';
    const points = Number(this.challengePoints[ctfId]);

    if (!name || !description || !flag || !Number.isInteger(points) || points < 1 || points > 10000) {
      return;
    }
    if (file && file.size > 25 * 1024 * 1024) {
      this.errorMessage.set('Puzzle files must be 25 MB or smaller.');
      return;
    }

    // FormData lets one request send regular fields and an optional puzzle file together.
    const body = new FormData();
    body.append('name', name);
    body.append('description', description);
    body.append('flag', flag);
    body.append('points', String(points));
    if (file) body.append('file', file);

    this.creatingChallengeCtfId.set(ctfId);
    this.errorMessage.set('');
    this.noticeMessage.set('');
    this.http
      .post<Challenge>(`${this.apiUrl}/ctfs/${ctfId}/challenges`, body, { withCredentials: true })
      .subscribe({
        next: (challenge) => {
          // The API response contains public puzzle details but never contains the expected flag.
          this.ctfs.update((current) => current.map((ctf) =>
            ctf.id === ctfId ? { ...ctf, challenges: [challenge, ...ctf.challenges] } : ctf,
          ));
          delete this.challengeNames[ctfId];
          delete this.challengeDescriptions[ctfId];
          delete this.challengeFlags[ctfId];
          this.challengePoints[ctfId] = 100;
          this.noticeMessage.set(`Puzzle published${file ? ` with ${file.name}` : ''}.`);
          this.creatingChallengeCtfId.set(null);
        },
        error: (error: HttpErrorResponse) => {
          this.setApiError('publish this puzzle', error.status);
          this.creatingChallengeCtfId.set(null);
        },
      });
  }

  protected submitFlag(ctfId: string, challengeId: string): void {
    const flag = this.flagAnswers[challengeId]?.trim() ?? '';
    if (!flag) return;

    this.submittingChallengeId.set(challengeId);
    this.errorMessage.set('');
    this.http
      .post<FlagSubmissionResult>(
        `${this.apiUrl}/ctfs/${ctfId}/challenges/${challengeId}/submit`,
        { flag },
        { withCredentials: true },
      )
      .subscribe({
        next: (result) => {
          this.challengeFeedback.update((current) => ({ ...current, [challengeId]: result.message }));
          if (result.correct) {
            // Only mark the puzzle solved after the server checks the answer.
            this.ctfs.update((current) => current.map((ctf) => ctf.id === ctfId
              ? {
                  ...ctf,
                  challenges: ctf.challenges.map((challenge) =>
                    challenge.id === challengeId ? { ...challenge, isSolved: true } : challenge,
                  ),
                }
              : ctf,
            ));
            delete this.flagAnswers[challengeId];
          }
          this.submittingChallengeId.set(null);
        },
        error: () => {
          this.challengeFeedback.update((current) => ({
            ...current,
            [challengeId]: 'Could not check this flag. Please try again.',
          }));
          this.submittingChallengeId.set(null);
        },
      });
  }

  protected challengeFileUrl(ctfId: string, challengeId: string): string {
    // Files are served through an authorized API route instead of a public static folder.
    return `${this.apiUrl}/ctfs/${ctfId}/challenges/${challengeId}/file`;
  }

  private loadCtfs(): void {
    this.http
      .get<CtfEvent[]>(`${this.apiUrl}/ctfs`, { withCredentials: true })
      .subscribe({
        next: (ctfs) => {
          // Give each event's point field a useful starting value when its form first appears.
          for (const ctf of ctfs) this.challengePoints[ctf.id] ??= 100;
          this.ctfs.set(ctfs);
        },
        error: (error: HttpErrorResponse) => this.setApiError('load CTFs', error.status),
      });
  }

  private setApiError(action: string, status: number): void {
    // A 404 usually means the API process was started before the new routes were built.
    this.errorMessage.set(status === 404
      ? 'The AuthApi needs a restart to load the CTF challenge endpoints.'
      : `Could not ${action}. Please try again.`);
  }

  private checkSession(): void {
    // This lets a signed-in user refresh the page without having to log in again.
    this.http
      .get<CurrentUser>(`${this.apiUrl}/auth/me`, { withCredentials: true })
      .subscribe({
        next: (user) => {
          this.user.set(user);
          this.loadCtfs();
          this.checkingSession.set(false);
        },
        error: () => this.checkingSession.set(false),
      });
  }
}
