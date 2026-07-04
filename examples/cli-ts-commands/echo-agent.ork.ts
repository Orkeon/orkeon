/// <reference path="./.orkeon/orkeon.d.ts" />
//
// Companion agent for dispatch.cmd.ts (design §8 item 9). An agent declares `onCommand` to
// become the finisher of a dispatched command: the value it returns is the response that
// terminates the command (sync `request` resolves to it; async `completed` replays it).
//
// The agent must be activated on the dispatch bus (AgentCommandRegistrar) so `ask`/`ask-bg`
// can reach it by the name "echo".

const echo = agentBuilder()
  .name("echo")
  .role("Echo")
  .goal("Echo a payload back, transformed")
  // Answer the "run" intent; uppercases the payload.
  .onCommand("run", (env) => env.payload.toUpperCase())
  // Catch-all for any other intent.
  .onCommand((env) => ({ success: true, payload: "ack:" + env.intent }))
  .build();

export { echo };
