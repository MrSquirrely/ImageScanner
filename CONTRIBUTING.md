# Contributing to ImageScanner

First off, thank you for considering contributing to the project! We welcome community contributions to help make this the best image organization tool possible. 

To ensure a smooth collaboration process, please read and follow these guidelines before submitting an issue or a pull request.

## Core Contribution Rules

### 1. One Issue Per Pull Request
To keep code reviews manageable and project history clean, **you must only fix or address one issue at a time per Pull Request.** 
* If your PR attempts to fix multiple, unrelated issues at once, **it will be denied.** 
* If you have multiple fixes or features, please submit them as separate Pull Requests.

### 2. Coding Guidelines & `.editorconfig`
All code contributions must strictly adhere to the project's coding guidelines. 
* To make this seamless, we have included an `.editorconfig` file in the root of the repository. 
* Please ensure your IDE (Visual Studio, Rider, VS Code, etc.) is configured to read and apply the `.editorconfig` rules automatically so your formatting, spacing, and naming conventions match the existing codebase.

### 3. AI Generation Policy
We embrace modern development tools, and **AI code generation is allowed.** However, quality is paramount:
* **Code:** If you submit AI-generated code, you must manually test, review, and clean it up before opening a PR. If a PR contains obvious hallucinations, bloated logic, or unverified AI output with no testing, **it will be denied.**
* **Documentation:** Generating documentation, XML comments, and markdown files using AI is completely acceptable and highly encouraged.

---

## Standard Development Workflow

### 1. Branching
* Fork the repository and clone it to your local machine.
* Create a new branch for your work. Use a descriptive naming convention:
  * `feature/issue-number-short-description` (e.g., `feature/12-add-comic-grouping`)
  * `bugfix/issue-number-short-description` (e.g., `bugfix/34-fix-thumbnail-crash`)

### 2. Committing
* Write clear, concise commit messages. 
* Start your commit message with a capitalized verb in the imperative mood (e.g., "Add search filter for categories" instead of "Added search filter" or "Adding search").
* Reference the issue number in the commit message if applicable (e.g., "Fix database lock during deep scan (#45)").

### 3. Testing
* Ensure your code compiles without warnings.
* Run existing tests to ensure your changes haven't broken current functionality.
* If you are adding a new feature, manually test it thoroughly (and include automated tests if the architecture supports it).

### 4. Opening a Pull Request
* Push your branch to your forked repository.
* Open a Pull Request against the `main` branch of the upstream repository.
* Fill out the PR template completely. Clearly describe the problem you are solving, the approach you took, and any specific areas you'd like reviewers to look at.
* Link the PR to the issue it resolves.

## Code of Conduct
By participating in this project, you agree to abide by the project's [Code of Conduct](CODE_OF_CONDUCT.md). Please ensure all interactions remain professional, inclusive, and respectful.
