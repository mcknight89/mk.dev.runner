console.log("about to crash");

setTimeout(() => {
  console.error("something went wrong");
  process.exit(1);
}, 1000);
