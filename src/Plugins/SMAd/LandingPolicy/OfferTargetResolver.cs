using Microsoft.Playwright;

namespace SMAd.LandingPolicy;

/// <summary>Pure site recognition. Resolving a target never types, submits or navigates.</summary>
public static class OfferTargetResolver
{
    public static ILocator? Resolve(IPage page)
    {
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        var selector = host switch
        {
            "m.p4psearch.1688.com" => "div[class^='offer-item']",
            "m.1688.com" =>           "div[class^='offer-item']",
            "b2b.baidu.com" => ".img-content,.list-title,.content-without-title,a.product-item-link",
            "sjh.baidu.com" => ".adcard-image-text,.bottom-consult-button,.adcard-row-one-root",
            "uland.taobao.com" => "a[class^='link']",
            "pro.m.jd.com" => ".masonryCard,.commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container",
            "m.jd.com" => ".commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container",
            _ => null
        };
        return selector == null ? null : page.Locator(selector);
    }
}
