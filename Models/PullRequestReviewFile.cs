namespace CodeAissure.Models
{
    public struct PullRequestReviewFile
    {
        public PullRequestReviewFile(string fileName, string description, string comments)
        {
            FileName = fileName;
            Description = description;
            Comments = comments;
        }

        public string FileName { get; init; }
        public string Description { get; init; }
        public string Comments { get; init; }
    }
}
