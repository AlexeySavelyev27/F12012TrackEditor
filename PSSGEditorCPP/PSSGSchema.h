#pragma once
#include <string>
#include <unordered_map>
#include <vector>

struct PSSGSchema {
    std::unordered_map<uint32_t, std::string> node_id_to_name;
    std::unordered_map<std::string, uint32_t> node_name_to_id;
    std::unordered_map<uint32_t, std::unordered_map<uint32_t, std::string>> attr_id_to_name;
    std::unordered_map<std::string, std::unordered_map<std::string, uint32_t>> attr_name_to_id;

    void BuildFromTree(const struct PSSGNode& root);
};
