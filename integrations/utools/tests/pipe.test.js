const test = require("node:test");
const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const { StringDecoder } = require("node:string_decoder");
const fs = require("node:fs");
const vm = require("node:vm");

function client(chunks, failure) {
  let calls = 0;
  const socket = new EventEmitter();
  let decoder;
  socket.setEncoding = encoding => { decoder = new StringDecoder(encoding); };
  socket.destroy = () => socket.emit("close");
  socket.write = () => {
    if (failure === "after") return socket.emit("error", new Error("lost"));
    for (const chunk of chunks) socket.emit("data", decoder ? decoder.write(chunk) : chunk);
    if (decoder) socket.emit("data", decoder.end());
    socket.emit("end");
  };
  const context = {
    window: {}, setTimeout, clearTimeout, process,
    require(name) {
      if (name === "net") return { createConnection() {
        queueMicrotask(() => failure === "before" ? socket.emit("error", new Error("missing")) : socket.emit("connect"));
        return socket;
      }};
      if (name === "child_process") return {
        execFileSync: () => '"node","1","Console","1","0"',
        execFile: (_exe, _args, _opts, cb) => { calls++; cb(null, "fallback\r\n"); }
      };
      return require(name);
    }
  };
  vm.runInNewContext(fs.readFileSync(require("node:path").join(__dirname, "../preload.js"), "utf8"), context);
  return { context, calls: () => calls };
}

test("all UTF-8 split boundaries preserve workspace names", async () => {
  const name = " 项目 仕事 업무 مشروع Cafe\u0301 🚀 ";
  const bytes = Buffer.from("ok\r\n" + name + "\r\n");
  for (let i = 1; i < bytes.length; i++) {
    const c = client([bytes.subarray(0,i), bytes.subarray(i)]);
    const names = await c.context.fetchWorkspaces("wt.exe");
    assert.deepEqual(Array.from(names), [name]);
    assert.equal(c.calls(), 0);
  }
});

test("fallback only before sending; empty and error replies are final", async () => {
  for (const [chunks, failure, expected, count] of [
    [[], "before", ["fallback"], 1],
    [[], "after", [], 0],
    [[Buffer.from("ok\r\n\r\n")], null, [], 0],
    [[Buffer.from("error\r\nunknown\r\n")], null, [], 0]
  ]) {
    const c = client(chunks, failure);
    assert.deepEqual(Array.from(await c.context.fetchWorkspaces("wt.exe")), expected);
    assert.equal(c.calls(), count);
  }
});
