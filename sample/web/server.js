const http = require("http");

const port = process.env.PORT || 5181;

http.createServer((req, res) => res.end("Sample web is running")).listen(port, () => {
  console.log(`ready on http://localhost:${port}`);
});
