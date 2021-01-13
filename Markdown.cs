using Markdig;
using Markdig.SyntaxHighlighting;

namespace StaticSiteGenerator
{
    public class MarkdownProcessor
    {
        // Configure the pipeline with all advanced extensions active
        private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions()
                                                                                   //.UseSyntaxHighlighting()
                                                                                   .Build();

        public string Transform(string markdown)
        {
            return Markdown.ToHtml(markdown, _pipeline);
        }
    }
}
