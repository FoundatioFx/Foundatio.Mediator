/** Serialize monitoring reads and coalesce explicit refreshes/navigation while a read is pending. */
export class QueueRefresh {
  pending = $state(false);
  manual = $state(false);
  inspecting = $state(false);
  #inFlight: Promise<void> | null = null;
  #again = false;
  #inspectNext = false;
  #stopped = false;

  constructor(private load: (inspect: boolean) => Promise<void>) {}

  request({
    manual = false,
    inspect = false,
    changed = false
  } = {}): Promise<void> {
    if (this.#stopped) return Promise.resolve();
    this.manual ||= manual;
    this.inspecting ||= inspect;
    if (this.#inFlight) {
      if (manual || inspect || changed) {
        this.#again = true;
        this.#inspectNext ||= inspect;
      }
      return this.#inFlight;
    }
    this.pending = true;
    this.#inFlight = this.#run(inspect).finally(() => {
      this.#inFlight = null;
      this.pending = false;
      this.manual = false;
      this.inspecting = false;
    });
    return this.#inFlight;
  }

  async #run(inspect: boolean) {
    do {
      this.#again = false;
      this.#inspectNext = false;
      await this.load(inspect);
      inspect = this.#inspectNext;
    } while (this.#again && !this.#stopped);
  }

  wait(): Promise<void> {
    return this.#inFlight ?? Promise.resolve();
  }

  stop() {
    this.#stopped = true;
  }
}
