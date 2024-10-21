using System.CommandLine;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using CodeAissure.Models;
using LibGit2Sharp;
using OpenAI.Chat;

namespace CodeAissure
{
    internal class Program
    {
        private static readonly string[] ignoreExtensions = [".csproj", ".sln", ".bak", ".dll", ".exe", ".lock.json"];

        public static Task<int> Main(string[] args)
        {
            Option<string> apikeyOption = new(
                                new[] { "--apikey", "-k" },
                                "The OpenAI API key");
            Option<string> apiendpointOption = new(
                                new[] { "--apiendpoint", "-a" },
                                "The OpenAI API endpoint (azure only)");
            Option<string> modelOption = new(
                                new[] { "--model", "-m" },
                                getDefaultValue: () => "gpt-4-32k",
                                "The OpenAI model to use for the API call.");
            Option<string> repoOption = new(
                                new[] { "--repopath", "-r" },
                                "The path to the Git repository");
            Option<string?> outputOption = new(
                                new[] { "--output", "-o" },
                                "Filename to output contents.");
            Option<string> fromBranchOption = new(
                                new[] { "--base", "-b" },
                                "The name of the base Git branch.");
            Option<string> targetBranchOption = new(
                                new[] { "--target", "-t" },
                                "The name of the compare Git branch");
            Option<int> maxFileTokens = new(
                                new[] { "--max-tokens", "-mt" },
                                getDefaultValue: () => 25000,
                                "The maximum number of charachters per diff to send to openai");
            Command reviewCommand = new("reviewchanges", "Create a review for changes between to branches") { apikeyOption, apiendpointOption, modelOption, repoOption, fromBranchOption, targetBranchOption, maxFileTokens, outputOption };
            // Parse the command-line arguments
            RootCommand rootCommand = new("OpenAI-based code review tool");
            rootCommand.AddCommand(reviewCommand);

            reviewCommand.SetHandler(GetPullRequestReviewAsync, apikeyOption, apiendpointOption, modelOption, repoOption, fromBranchOption, targetBranchOption, maxFileTokens, outputOption);

            return rootCommand.InvokeAsync(args);
        }

        private static async Task<string> GetChatResultsAsync(ChatClient client, string model, params ChatMessage[] messages)
        {
            System.ClientModel.ClientResult<ChatCompletion> result = await client.CompleteChatAsync(messages);
            StringBuilder responseTexts = new();
            foreach (ChatMessageContentPart? completion in result.Value.Content)
            {
                _ = responseTexts.Append(completion.Text);
            }
            return responseTexts.ToString();
        }

        private static async Task GetPullRequestReviewAsync(string apiKey, string apiendpoint, string model, string repoPath, string baseBranch, string compareBranch, int maxFileTokens, string? outputFile)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new ArgumentException($"'{nameof(apiKey)}' cannot be null or empty.", nameof(apiKey));
            }

            if (string.IsNullOrEmpty(apiendpoint))
            {
                throw new ArgumentException($"'{nameof(apiendpoint)}' cannot be null or empty.", nameof(apiendpoint));
            }

            if (string.IsNullOrEmpty(model))
            {
                throw new ArgumentException($"'{nameof(model)}' cannot be null or empty.", nameof(model));
            }

            if (string.IsNullOrEmpty(repoPath))
            {
                throw new ArgumentException($"'{nameof(repoPath)}' cannot be null or empty.", nameof(repoPath));
            }

            if (string.IsNullOrEmpty(baseBranch))
            {
                throw new ArgumentException($"'{nameof(baseBranch)}' cannot be null or empty.", nameof(baseBranch));
            }

            if (string.IsNullOrEmpty(compareBranch))
            {
                throw new ArgumentException($"'{nameof(compareBranch)}' cannot be null or empty.", nameof(compareBranch));
            }

            using TextWriter output = string.IsNullOrEmpty(outputFile) ? Console.Out : new StreamWriter(outputFile);

            // Open the Git repository
            using Repository repo = new(repoPath);
            // Retrieve the base branch and the compare branch
            Commit? baseCommit = repo.Branches[baseBranch]?.Tip;
            if (baseCommit == null)
            {
                output.Write($"Error!: Could not find branch {baseBranch} in repo {repoPath}");
                return;
            }
            Commit? compareCommit = repo.ObjectDatabase.FindMergeBase(baseCommit, repo.Branches[compareBranch]?.Tip);
            if (compareCommit == null)
            {
                output.Write($"Error!: Could not find branch {compareBranch} in repo {repoPath}");
                return;
            }

            // Get the diff between the two branches
            TreeChanges changes = repo.Diff.Compare<TreeChanges>(baseCommit.Tree, compareCommit.Tree);
            if (!changes?.Any() ?? false)
            {
                output.Write($"Warning: No changes found between {baseBranch} and {compareBranch} in repo {repoPath}");
                return;
            }

            List<string> results = [];

            // Call the OpenAI API to generate the description of the change
            AzureOpenAIClient client = new(new Uri(apiendpoint), new System.ClientModel.ApiKeyCredential(apiKey));
            ChatClient chat = client.GetChatClient(model);
            //await output.WriteLineAsync("[");
            List<PullRequestReviewFile> reviewdFiles = [];
            // Loop through each changed file

            foreach (TreeEntryChanges change in changes)
            {
                if (change.Status != ChangeKind.Unmodified)
                {
                    // Get the patch for the change
                    string? patch = repo.Diff.Compare<Patch>(baseCommit.Tree, compareCommit.Tree, new[] { change.Path }).FirstOrDefault()?.Patch;
                    if (string.IsNullOrEmpty(patch))
                    {
                        continue;
                    }

                    // Get the short description of the change
                    string fileName = Path.GetFileName(change.Path);
                    //TODO: make this configurable eg by .aissureignore file
                    if (ignoreExtensions.Any(fileName.EndsWith))
                    {
                        continue;
                    }

                    List<string> patchparts = StringChunker.SplitStringIntoChunks(patch, maxFileTokens);
                    string basePrompt = patchparts.Count > 1 ? Prompts.SendPartOfPatch : Prompts.SendPatch;
                    StringBuilder description = new();
                    StringBuilder review = new();
                    foreach (string patchpart in patchparts)
                    {
                        string responseTexts = await GetChatResultsAsync(chat, model,
                                    new SystemChatMessage(Prompts.SystemMessage),
                                    new SystemChatMessage(Prompts.PatchSystemMessage),
                                    new UserChatMessage(basePrompt.Replace("$filename", fileName).Replace("$file_diff", patchpart)));

                        string[] responseParts = responseTexts.Split("--ENDOFDESC--");
                        if (responseParts.Length > 0)
                        {
                            _ = description.AppendLine(responseParts[0].Trim());
                        }
                        if (responseParts.Length > 1)
                        {
                            _ = review.AppendLine(responseParts[1].Replace("Review:", "").Replace("LGTM!", "").Trim());
                        }
                    }

                    string finalDescription = patchparts.Count > 1
                        ? await GetChatResultsAsync(chat, model,
                            new SystemChatMessage(Prompts.SystemMessage),
                            new UserChatMessage(Prompts.SummarizeMultiPart.Replace("$numparts", patchparts.Count.ToString()).Replace("$descriptions", description.ToString().Trim())))
                        : description.ToString();

                    PullRequestReviewFile reviewedFile = new(change.Path, finalDescription.Trim(), review.ToString().Trim());

                    reviewdFiles.Add(reviewedFile);
                }
            }
            //await output.WriteLineAsync("]");

            string summary = await GetChatResultsAsync(chat, model, new SystemChatMessage(Prompts.SystemMessage), new SystemChatMessage(Prompts.SummarizeTotalSystemMessage), new UserChatMessage(JsonSerializer.Serialize(reviewdFiles)));
            await output.WriteLineAsync(summary);

            await output.WriteLineAsync("\n\n### The raw json output for the file reviews was: ");
            await output.WriteLineAsync("\n```json");
            await output.WriteLineAsync(JsonSerializer.Serialize(reviewdFiles, new JsonSerializerOptions { WriteIndented = true }));
            await output.WriteLineAsync("```");
        }
    }
}
