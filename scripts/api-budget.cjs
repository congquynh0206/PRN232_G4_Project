// Estimates supplied costs; does not query billing or change a cluster.
function estimate({ budget, fixed, hourly, hours = 730, reserve = 0.1 }) {
  for (const [name, value] of Object.entries({ budget, fixed, hourly, hours, reserve })) {
    if (!Number.isFinite(value) || value < 0) throw new Error(`Invalid ${name}`);
  }
  if (hourly === 0 || hours === 0 || reserve >= 1) {
    throw new Error('hourly/hours must be positive; reserve must be below 1');
  }
  const usable = budget * (1 - reserve);
  const affordable = Math.max(0, Math.min(4, Math.floor((usable - fixed) / (hourly * hours))));
  return {
    budget, usable, fixed, hourly, hours, reserve,
    maxApiReplicas: affordable,
    maxReadReplicas: Math.max(0, affordable - 1),
    topologyAffordable: affordable >= 2,
    minimumCost: fixed + 2 * hourly * hours,
    fourApiCost: fixed + 4 * hourly * hours,
    remainingAfterFour: usable - fixed - 4 * hourly * hours,
  };
}

module.exports = { estimate };
if (require.main === module) {
  try {
    const args = process.argv.slice(2).map(Number);
    if (args.length < 3 || args.length > 5) {
      throw new Error('Usage: node scripts/api-budget.cjs budget fixed hourly [hours=730] [reserve=0.1]');
    }
    const [budget, fixed, hourly, hours = 730, reserve = 0.1] = args;
    const result = estimate({ budget, fixed, hourly, hours, reserve });
    console.log(JSON.stringify(result, null, 2));
    if (!result.topologyAffordable) process.exitCode = 2;
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
