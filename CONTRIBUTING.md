# Contributing to Rebirth

Thanks for wanting to help! Ideas, bug reports and pull requests are all welcome.

## Ideas and bugs

- **Found a bug?** Open an issue with the *Bug report* template. Screenshots help a lot.
- **Have an idea?** Open an issue with the *Idea* template, or start a thread in Discussions if you'd like to
  talk it through first. Check it against the pillars in `DESIGN.md`: cosy, no time pressure, readable,
  growth you can watch.

## Making a change

1. **Fork** the repository and clone your fork.
2. Install **Godot 4.7 (.NET)**, the **.NET 8 SDK** and **Git LFS** (run `git lfs install` once), open
   `project.godot`, and press F5 to play.
3. Create a branch for your change, named after you and the topic: `git checkout -b yourname/my-change`.
4. Build and try it:
   - `dotnet build` must pass. The automatic check on your pull request runs the same.
   - Play the part you changed, or drive it with a smoke script (see `AGENTS.md`).
5. Update `DESIGN.md` if you added or changed a feature.
6. Push to your fork and open a **pull request** against `master`. Fill in the template: what changed, how you
   tested it, and a screenshot or short clip for anything visible.

Keep pull requests focused: one feature or fix each is much easier to review than a big mix.

## Assets

Game-ready files (`.glb` models, `.png` textures, `.ogg`/`.wav` sounds, fonts) go into the repo through Git LFS;
`.gitattributes` handles that automatically. Source files (`.blend`, `.psd`) stay out of git: keep them on the
team's shared drive.

## Working with AI assistants

Most of this project was built together with an AI coding assistant, and that's a fine way to contribute.
Point your assistant at **`AGENTS.md`**: it explains the code map, conventions, how to test by driving the
real game, and the pitfalls we already found. Please still read and try what it produces before opening a
pull request.

## Style

- C#, tabs, comments that explain *why*. Follow the patterns already in the code.
- The look is warm, pastel, and toy-like. Light only comes from lamps and flames, and there is no neon.
- UI text is in English.

By contributing you agree that your work is released under the project's [MIT license](LICENSE).
