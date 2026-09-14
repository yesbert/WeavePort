## Research and editorial decisions

Reviewed on 2026-09-14:

- [Blume landing page](https://useblume.dev/) and [introduction](https://useblume.dev/docs): a focused promise, visible product example, quiet navigation and a persistent grouped sidebar. Adopt the hierarchy and restraint, not its copy or product claims.
- [Extism](https://extism.org/): makes the cross-language use case immediately concrete. State our .NET host and three plugin languages explicitly; do not borrow its WebAssembly sandbox claims.
- [Docusaurus](https://docusaurus.io/): ties features to what authors can get done and gives a prominent path into documentation.
- [Astro documentation](https://docs.astro.build/en/getting-started/): separates getting started, guides and reference so readers can enter at the right level.
- [GitHub README guidance](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes): explain purpose, usefulness, getting started and help before repository detail.
- [Nielsen Norman Group's web-reading research](https://www.nngroup.com/articles/how-users-read-on-the-web/): use descriptive headings, short paragraphs and objective statements that readers can scan. These observations inform design choices; no conversion improvement is claimed without user testing.

## Positioning

Primary audience: developers building a .NET product with owner-approved extension code. Lead with document readers, evaluation strategies and scheduling rules. Explain language flexibility, granted access and shared worker management in terms of application work. Keep macOS qualification, evolving 0.1.0 API and trusted native execution visible near the adoption path.

Show minimal echo provider excerpts grounded in the maintained SDK examples. Label the contract preview as illustrative, not a live runtime demo. The complete sample remains the primary call to action. Avoid fabricated customer logos, star counts, testimonials, speed claims and arbitrary production-readiness badges.

## Presentation and information architecture

Retain DocFX and the existing original logo. Use a centered, concise hero, blue accents on a neutral surface, a code/contract showcase, scannable benefits and use-case examples. The logo identifies the product in the header instead of occupying the main reading area. Documentation gets a dedicated reference TOC and left sidebar; the header has a small number of destinations.

Retain canonical technical details and specifications. Improve their introductory framing where helpful; do not turn exact operational rules into marketing summaries. Keep implementation rationale here, outside the product flow.

## Verification

Build with warnings as errors; validate links, anchors, discovery files and output privacy. Check language tabs by mouse and keyboard, no-script fallback, responsive widths, dark mode and navigation/search. Verify example text against maintained sources and run the existing documentation/OpenSpec checks. Prepare a checksummed staging artifact, without implying public activation.

## Editorial surface review

All maintained guide introductions were reviewed. Updated thirteen consumer-facing guides to introduce the reader's task before implementation detail: SDK authoring, coordinator, installed artifacts, architecture, native recovery, local execution, lifecycle, compatibility, diagnostics, bulk composition, protocol, AI retrieval and benchmarks. The SDK guide now names the public 0.1.0 package; optional Composition is explicitly outside that release.

Kept the precise status, security, platform, distribution, release, soak and integration-contract descriptions as references. Engineering/framework guidance remains contributor material, not marketing copy. The three sample walkthroughs already describe a concrete running result; the README and website now make their use cases easier to discover without rewriting their evidence. The public package readme retains its exact contract and execution limits. Imprint and privacy content remain factual rather than promotional.

The new documentation routes sit under `/docs/`, with a single grouped reference TOC. This changes the earlier unpublished staging layout; no public URL migration is claimed. Root navigation is reduced to Docs and Releases, alongside search and the source link.

## Verified outcome

The build produces 37 content pages and two TOC documents without warnings. All 460 static local references resolve. Six regression checks cover nested routes, assets, missing-source rejection, LLM HTML link rewriting and the source identity of all three language examples. Existing documentation, OpenSpec and public-tree checks pass.

Inspected the landing page and documentation at desktop and 390-pixel mobile widths, including dark mode. Language switching worked by click and arrow key. Mobile contents navigation opened and reached the Quickstart; search returned relevant scheduling results. No horizontal document overflow was observed on the inspected mobile views, and the browser reported no JavaScript errors. The static markup keeps every provider visible until the tab enhancement starts; this fallback was reviewed in source, not claimed as a separate JavaScript-disabled browser run.

A replacement archive and source-file hash manifest were staged outside public document roots; the remote checksum matched. Public activation remains pending. The new messaging and layout have not undergone conversion testing, and no measured adoption uplift is claimed.
