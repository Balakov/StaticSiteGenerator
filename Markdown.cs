using Markdig;

namespace StaticSiteGenerator
{
    public class MarkdownProcessor
    {
        // Configure the pipeline with all advanced extensions active
        private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions()
                                                                                   .Build();

        public string Transform(string markdown)
        {
            return Markdown.ToHtml(markdown, _pipeline);
        }
    }
}
