using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AI.Dev.OpenAI.GPT;
using static System.Net.Mime.MediaTypeNames;

class StringChunker
{
    public static List<string> SplitStringIntoChunks(string input, int maxTokens = 2024)
    {
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        var currentTokenCount = 0;

        using (var reader = new StringReader(input))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                int lineTokenCount = CountTokens(line);

                if (currentTokenCount + lineTokenCount > maxTokens)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                    currentTokenCount = 0;
                }

                currentChunk.AppendLine(line);
                currentTokenCount += lineTokenCount;
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }

        return chunks;
    }

    private static int CountTokens(string line)
    {
        return GPT3Tokenizer.Encode(line).Count;
    }
}
