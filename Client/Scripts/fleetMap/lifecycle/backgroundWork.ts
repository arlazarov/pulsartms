// Network promises alone do not let the browser paint between CPU-heavy stages.
export function yieldToBrowser(): Promise<void> {
  const scheduler = (
    globalThis as {
      scheduler?: {
        postTask(
          task: () => void,
          options: { priority: string },
        ): Promise<void>;
      };
    }
  ).scheduler;
  if (scheduler?.postTask)
    return scheduler.postTask(() => {}, { priority: 'background' });
  return new Promise(resolve => setTimeout(resolve, 0));
}
