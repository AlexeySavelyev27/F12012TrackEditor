#pragma once
#include <string>
#include <vector>
#include <unordered_map>

struct PSSGNode {
    std::string name;
    std::unordered_map<std::string, std::vector<unsigned char>> attributes;
    std::vector<PSSGNode> children;
    std::vector<unsigned char> data;
};
