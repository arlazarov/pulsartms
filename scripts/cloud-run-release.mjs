import { readFileSync } from "node:fs";

const [kind, name, expected] = process.argv.slice(2);

function ready(resource) {
  return resource.status?.conditions?.some(
    (condition) => condition.type === "Ready" && condition.status === "True",
  );
}

function pinnedTraffic(targets) {
  if (!Array.isArray(targets) || targets.length === 0) return false;
  let total = 0;
  for (const target of targets) {
    const percent = target.percent ?? 0;
    if (!Number.isInteger(percent) || percent < 0 || percent > 100) {
      return false;
    }
    if (
      percent > 0 &&
      (target.revisionName !== expected || target.latestRevision === true)
    ) {
      return false;
    }
    total += percent;
  }
  return total === 100;
}

try {
  const resource = JSON.parse(readFileSync(0, "utf8"));
  if (
    !name ||
    !expected ||
    resource.metadata?.name !== name ||
    !ready(resource)
  ) {
    throw new Error(
      "Cloud Run resource identity or readiness is not verified.",
    );
  }
  if (kind === "revision") {
    const containers = resource.spec?.containers;
    if (
      containers?.length !== 1 ||
      containers[0].image !== expected ||
      resource.status.imageDigest !== expected
    ) {
      throw new Error(
        "Cloud Run revision does not match the built image digest.",
      );
    }
  } else if (kind === "traffic") {
    if (
      !Number.isInteger(resource.metadata.generation) ||
      resource.status.observedGeneration !== resource.metadata.generation ||
      !pinnedTraffic(resource.spec?.traffic) ||
      !pinnedTraffic(resource.status.traffic)
    ) {
      throw new Error(
        "Cloud Run has not confirmed 100% traffic on this revision.",
      );
    }
  } else {
    throw new Error("Unknown Cloud Run release verification operation.");
  }
} catch (error) {
  const message =
    error instanceof SyntaxError
      ? "Cloud Run returned invalid verification data."
      : error.message;
  console.error(`${message} Deployment not confirmed.`);
  process.exitCode = 1;
}
