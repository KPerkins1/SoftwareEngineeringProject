import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { App } from './app';

// These tests use a fake HTTP backend so they do not need a running API server.
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    })
      .compileComponents();
  });

  it('publishes a puzzle and checks submitted flags', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('http://localhost:5000/api/auth/me').flush({
      id: '1',
      email: 'demo@example.com',
      name: 'Demo User',
    });
    http.expectOne('http://localhost:5000/api/ctfs').flush([]);
    fixture.detectChanges();

    // The test starts with a signed-in user and an empty CTF library.
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Capture the flags.');

    const component = fixture.componentInstance as unknown as {
      ctfName: string;
      createCtf: () => void;
      challengeNames: Record<string, string>;
      challengeDescriptions: Record<string, string>;
      challengeFlags: Record<string, string>;
      challengePoints: Record<string, number>;
      flagAnswers: Record<string, string>;
      editCtfName: string;
      editCtfDescription: string;
      editChallengeName: string;
      editChallengeDescription: string;
      editChallengeFlag: string;
      editChallengePoints: number;
      startEditCtf: (ctf: { id: string; name: string; description: string }) => void;
      saveCtf: (ctfId: string) => void;
      startEditChallenge: (ctfId: string, challenge: { id: string; name: string; description: string; points: number }) => void;
      saveChallenge: (ctfId: string, challengeId: string, file: File | undefined) => void;
      deleteChallenge: (ctfId: string, challenge: { id: string; name: string }) => void;
      deleteCtf: (ctf: { id: string; name: string }) => void;
      createChallenge: (ctfId: string, file: File | undefined) => void;
      submitFlag: (ctfId: string, challengeId: string) => void;
    };
    component.ctfName = 'Night Shift';
    component.createCtf();

    // Creating an event should send its form values to the CTF endpoint.
    const createRequest = http.expectOne('http://localhost:5000/api/ctfs');
    expect(createRequest.request.method).toBe('POST');
    expect(createRequest.request.body).toEqual({ name: 'Night Shift', description: '' });
    const ctf = {
      id: 'ctf-1',
      name: 'Night Shift',
      description: '',
      createdAt: '2026-11-03T00:00:00Z',
      challenges: [],
    };
    createRequest.flush(ctf);
    fixture.detectChanges();

    expect(compiled.querySelector('.event-item h3')?.textContent).toContain('Night Shift');

    component.challengeNames['ctf-1'] = 'Hidden Signal';
    component.challengeDescriptions['ctf-1'] = 'Decode the attached transmission.';
    component.challengeFlags['ctf-1'] = 'FLAG{signal_found}';
    component.challengePoints['ctf-1'] = 250;
    const file = new File(['encoded puzzle'], 'signal.txt', { type: 'text/plain' });
    component.createChallenge('ctf-1', file);

    // Puzzle details and the optional file travel together as multipart form data.
    const challengeRequest = http.expectOne('http://localhost:5000/api/ctfs/ctf-1/challenges');
    expect(challengeRequest.request.method).toBe('POST');
    const body = challengeRequest.request.body as FormData;
    expect(body.get('name')).toBe('Hidden Signal');
    expect(body.get('flag')).toBe('FLAG{signal_found}');
    expect((body.get('file') as File).name).toBe('signal.txt');
    const challenge = {
      id: 'challenge-1',
      name: 'Hidden Signal',
      description: 'Decode the attached transmission.',
      points: 250,
      fileName: 'signal.txt',
      fileSize: file.size,
      isSolved: false,
    };
    challengeRequest.flush(challenge);
    fixture.detectChanges();

    expect(compiled.querySelector('.challenge-card')?.textContent).toContain('Hidden Signal');
    expect(compiled.querySelector('.challenge-card')?.textContent).not.toContain('FLAG{signal_found}');

    component.flagAnswers['challenge-1'] = 'wrong-answer';
    component.submitFlag('ctf-1', 'challenge-1');
    // A wrong guess should show feedback without marking the puzzle solved.
    const wrongFlagRequest = http.expectOne('http://localhost:5000/api/ctfs/ctf-1/challenges/challenge-1/submit');
    expect(wrongFlagRequest.request.body).toEqual({ flag: 'wrong-answer' });
    wrongFlagRequest.flush({ correct: false, message: 'Incorrect flag. Try again.', points: 0 });
    fixture.detectChanges();
    expect(compiled.querySelector('.challenge-feedback')?.textContent).toContain('Incorrect flag');

    component.flagAnswers['challenge-1'] = 'FLAG{signal_found}';
    component.submitFlag('ctf-1', 'challenge-1');
    // A correct guess should update the puzzle to its solved state.
    const correctFlagRequest = http.expectOne('http://localhost:5000/api/ctfs/ctf-1/challenges/challenge-1/submit');
    correctFlagRequest.flush({ correct: true, message: 'Correct flag!', points: 250 });
    fixture.detectChanges();

    expect(compiled.querySelector('.solved-label')?.textContent).toContain('Solved');

    // Saving event edits should send a PUT and show the returned event details.
    component.startEditCtf(ctf);
    component.editCtfName = 'Night Shift Finals';
    component.editCtfDescription = 'Updated event details';
    component.saveCtf('ctf-1');
    const updateCtfRequest = http.expectOne('http://localhost:5000/api/ctfs/ctf-1');
    expect(updateCtfRequest.request.method).toBe('PUT');
    const updatedCtf = {
      ...ctf,
      name: 'Night Shift Finals',
      description: 'Updated event details',
      challenges: [{ ...challenge, isSolved: true }],
    };
    updateCtfRequest.flush(updatedCtf);
    fixture.detectChanges();
    expect(compiled.querySelector('.event-item h3')?.textContent).toContain('Night Shift Finals');

    // A blank replacement flag means the current answer should stay unchanged.
    component.startEditChallenge('ctf-1', challenge);
    component.editChallengeName = 'Hidden Signal 2';
    component.editChallengeDescription = 'Read the updated transmission.';
    component.editChallengeFlag = '';
    component.editChallengePoints = 300;
    component.saveChallenge('ctf-1', 'challenge-1', undefined);
    const updateChallengeRequest = http.expectOne('http://localhost:5000/api/ctfs/ctf-1/challenges/challenge-1');
    expect(updateChallengeRequest.request.method).toBe('PUT');
    const updateBody = updateChallengeRequest.request.body as FormData;
    expect(updateBody.get('flag')).toBe('');
    expect(updateBody.get('points')).toBe('300');
    updateChallengeRequest.flush({ ...challenge, name: 'Hidden Signal 2', points: 300, isSolved: true });
    fixture.detectChanges();
    expect(compiled.querySelector('.challenge-card h4')?.textContent).toContain('Hidden Signal 2');

    // After confirmation, each delete should call the API and remove the item from the view.
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    component.deleteChallenge('ctf-1', { id: 'challenge-1', name: 'Hidden Signal 2' });
    const deleteChallengeRequest = http.expectOne('http://localhost:5000/api/ctfs/ctf-1/challenges/challenge-1');
    expect(deleteChallengeRequest.request.method).toBe('DELETE');
    deleteChallengeRequest.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(compiled.querySelector('.challenge-card')).toBeNull();

    component.deleteCtf({ id: 'ctf-1', name: 'Night Shift Finals' });
    const deleteCtfRequest = http.expectOne('http://localhost:5000/api/ctfs/ctf-1');
    expect(deleteCtfRequest.request.method).toBe('DELETE');
    deleteCtfRequest.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(compiled.querySelector('.event-item')).toBeNull();
    http.verify();
  });
});
