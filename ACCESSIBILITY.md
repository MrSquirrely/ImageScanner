# Accessibility

At the Treehouse, we believe that organizing and viewing your images should be a seamless experience for everyone, regardless of ability or how you interact with your computer. This document outlines our long-term commitments to accessibility, what we expect from contributors, and how you can report barriers you encounter while using our application.

> ⚠️ **Version 1.0 Development Notice**
> ImageScanner is currently in active development towards its initial Version 1.0 release. While the guidelines below represent the future standards we intend to apply, it is currently impossible to ensure that every new feature strictly follows them during this rapid development phase. **Our roadmap strategy is to implement all core v1.0 features first, and then dedicate a development phase specifically to auditing and fixing accessibility issues.** We highly encourage you to report accessibility barriers now so we can track them, but please note that fixes may be delayed until the feature-complete milestone is reached.

## Priorities

Once the core features of Version 1.0 are complete, our primary goal is to ensure that ImageScanner's WPF desktop interface and console tools are fully usable via keyboard and compatible with standard Windows screen readers. While we use the Web Content Accessibility Guidelines (WCAG) 2.1 Level AA as an aspirational framework, our practical focus for the desktop experience will be:
* **Keyboard Navigation:** Ensuring every feature, from scanning folders to tagging images, can be reached and operated without a mouse.
* **Screen Reader Support:** Providing meaningful labels and context for UI elements so users relying on Windows Narrator or NVDA can navigate effectively.
* **Visual Clarity:** Respecting Windows High Contrast themes and ensuring standard text sizes are legible.

*Note: As this is a visual media application, automatically generating meaningful audio descriptions for the images themselves is outside our current scope, but navigating and managing the files must remain accessible.*

## Contributor expectations

During active v1.0 development, we ask that you keep these future accessibility standards in mind. Once we transition into the accessibility-focused phase, the following expectations will become strict requirements for all Pull Requests:
* **WPF UI Automation:** Use `AutomationProperties.Name` and `AutomationProperties.HelpText` on custom controls, buttons without text labels (like icon buttons), and complex layouts.
* **Keyboard Focus:** Ensure your new controls are reachable via the `Tab` key, have a visible focus indicator, and trap focus correctly within dialogs or overlays.
* **Testing:** Manually test user-facing changes using only your keyboard and, if possible, do a quick pass with Windows Narrator before submitting a Pull Request.

## Reporting accessibility issues

Even during our initial development phase, if you encounter a barrier that makes it difficult to use ImageScanner, we want to know so we can track it for our accessibility remediation phase. You can report an issue directly on our GitHub repository by opening a Bug Report and adding the `accessibility` label.

To help us track the issue accurately, please include:
* What you were trying to do (e.g., "Trying to add a tag to an image").
* The unexpected behavior you encountered.
* How you are interacting with the app (e.g., Keyboard only, Windows Narrator, NVDA, High Contrast mode).
* Your Operating System (Windows 10 or 11).

*You do not need to share any personal health information or disability disclosures when reporting an issue.*

### Severity

When we review accessibility issues, we categorize them to help prioritize fixes once feature-development concludes:

* **Critical:** A barrier that completely prevents a user from completing a core task (e.g., you cannot start a folder scan using the keyboard).
* **High:** A significant barrier that makes a task very difficult, though a workaround might exist (e.g., a button is unlabeled for screen readers, but can be triggered via a known keyboard shortcut).
* **Medium:** Issues that add frustration or cognitive load but don't stop the workflow (e.g., poor color contrast on secondary text).
* **Low:** Minor cosmetic or structural issues that do not directly impact user workflows.

### How we respond

When you report an accessibility issue, you can expect the following:
1. **Acknowledgement:** A maintainer will respond to your issue within 48 hours.
2. **Triage:** We will confirm the severity, add the appropriate labels, and log it in our post-v1.0 accessibility backlog. If there is a known workaround, we will share it with you immediately.
3. **Timeline:** Issues will remain open until we begin our dedicated accessibility phase. At that time, Critical and High severity issues will be prioritized first.
4. **Verification:** Once a fix is implemented, we will update the issue and ask you to verify that the barrier has been removed in the latest build.

## Ownership and maintenance

The core maintainers of ImageScanner are responsible for ensuring these future accessibility standards are upheld. Once Version 1.0 is feature-complete, accessibility reviews will become a strictly enforced part of our Pull Request review process. 

## Supported environments

ImageScanner is a desktop application built on .NET and WPF. When full accessibility support is rolled out, we will officially test and support features on:
* **Operating Systems:** Windows 10 (21H2+) and Windows 11 (21H2+).
* **Input Methods:** Standard mouse/pointer, and Keyboard-only navigation.
* **Assistive Technologies:** Windows Narrator (primary testing target) and NVDA.
* **Themes:** Default Windows Light/Dark modes and Windows High Contrast themes.

## Known limitations

Because ImageScanner is actively building out its v1.0 feature set, there are many known accessibility gaps. Expect that newly merged features may temporarily lack keyboard navigation traps, screen reader labels, or proper UI automation hooks until our targeted accessibility phase begins.

## Feedback and improvements

We are always looking to learn and improve. If you have suggestions for how we can make our future accessibility practices better, please open a Discussion or an Issue on our GitHub repository. Active barriers should be reported using the process outlined in the "Reporting accessibility issues" section above.
