// A single entry per layer group: no growing cache or retained historical frames.
export function memoizeLast() {
  let previous, value;
  return (dependencies, create) => {
    if (
      !previous ||
      dependencies.length !== previous.length ||
      dependencies.some((item, i) => item !== previous[i])
    ) {
      value = create();
      previous = dependencies;
    }
    return value;
  };
}
