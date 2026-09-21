// A single entry per layer group: no growing cache or retained historical frames.
export function memoizeLast<T>(): (
  dependencies: unknown[],
  create: () => T,
) => T {
  let previous: unknown[] | undefined, value: T;
  return (dependencies, create) => {
    if (
      !previous ||
      dependencies.length !== previous.length ||
      dependencies.some((item, i) => item !== previous![i])
    ) {
      value = create();
      previous = dependencies;
    }
    return value;
  };
}
