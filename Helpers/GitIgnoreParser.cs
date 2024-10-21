//using System.Linq;
//using System.Text.RegularExpressions;
//using Microsoft.Extensions.FileSystemGlobbing;
//using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

//namespace CodeAissure
//{
//    class GitIgnoreParser
//    {
//        private readonly List<Matcher> _ignoreMatchers = new List<Matcher>();

//        public GitIgnoreParser(string gitIgnoreFilePath)
//        {
//            if (File.Exists(gitIgnoreFilePath))
//            {
//                var lines = File.ReadAllLines(gitIgnoreFilePath);
//                foreach (var line in lines)
//                {
//                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
//                    {
//                        var matcher = new Matcher();
//                        matcher.AddInclude(line);
//                        _ignoreMatchers.Add(matcher);
//                    }
//                }
//            }
//        }

//        public bool IsIgnored(string filePath)
//        {
//            var fileInfo = new FileInfo(filePath);
//            var fileDirectory = fileInfo.DirectoryName;
//            var relativePath = fileInfo.FullName.Substring(fileDirectory.Length + 1);

//            foreach (var matcher in _ignoreMatchers)
//            {
//                var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(fileDirectory)));
//                if (result.HasMatches && result.Files.Contains(relativePath))
//                {
//                    return true;
//                }
//            }

//            return false;
//        }
//    }
//}
